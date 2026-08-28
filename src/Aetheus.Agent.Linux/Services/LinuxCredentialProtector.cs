// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Aetheus.Agent.Core.Services;

namespace Aetheus.Agent.Linux.Services;

public sealed class LinuxCredentialProtector : ICredentialProtector, IDisposable
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int SaltSize = 16;

    // Hardened key-material location: dedicated 0700 state dir outside the (often
    // world-readable 0755) install dir. Overridable via env var for tests only.
    private const string DefaultKeyStorageRoot = "/var/lib/aetheus-agent/keys";
    private const string KeyStorageRootOverrideEnvVar = "AETHEUS_AGENT_KEYDIR";

    private static string KeyStorageRoot =>
        Environment.GetEnvironmentVariable(KeyStorageRootOverrideEnvVar) is { Length: > 0 } overridePath
            ? overridePath
            : DefaultKeyStorageRoot;

    private readonly byte[] _key;
    private bool _disposed;

    public LinuxCredentialProtector()
    {
        _key = DeriveKey();
    }

    public byte[] Protect(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, ciphertext, tag);

        // Format: nonce || tag || ciphertext
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);
        return result;
    }

    public byte[] Unprotect(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid protected data.");

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var ciphertext = data.AsSpan(NonceSize + TagSize);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private static byte[] DeriveKey()
    {
        var machineId = GetMachineId();
        var installEntropy = GetOrCreateInstallEntropy();

        var combinedSecret = new byte[machineId.Length + installEntropy.Length];
        machineId.CopyTo(combinedSecret, 0);
        installEntropy.CopyTo(combinedSecret, machineId.Length);

        var salt = GetOrCreateSalt();
        var key = Rfc2898DeriveBytes.Pbkdf2(combinedSecret, salt, 600_000, HashAlgorithmName.SHA256, KeySize);

        CryptographicOperations.ZeroMemory(combinedSecret);
        return key;
    }

    private static byte[] GetOrCreateInstallEntropy()
    {
        var entropy = new byte[32];
        RandomNumberGenerator.Fill(entropy);
        return GetOrCreateKeyMaterial(".agent-entropy", entropy);
    }

    private static byte[] GetOrCreateSalt()
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return GetOrCreateKeyMaterial(".agent-salt", salt);
    }

    private static byte[] GetOrCreateKeyMaterial(string fileName, byte[] freshMaterial)
    {
        var path = GetKeyMaterialFilePath(fileName);

        if (File.Exists(path))
            return File.ReadAllBytes(path);

        // Migration: pre-hardening installs wrote key material under the (often
        // world-readable) install dir. Read it from there and migrate it to the
        // restricted location so already-deployed agents don't lose their keys.
        var legacyPath = GetLegacyKeyMaterialFilePath(fileName);
        if (File.Exists(legacyPath))
            return MigrateLegacyMaterial(legacyPath, path);

        WriteRestricted(path, freshMaterial);
        return freshMaterial;
    }

    internal static byte[] MigrateLegacyMaterial(string legacyPath, string restrictedPath)
    {
        var legacyMaterial = File.ReadAllBytes(legacyPath);
        WriteRestricted(restrictedPath, legacyMaterial);
        var migratedMaterial = File.ReadAllBytes(restrictedPath);
        if (!CryptographicOperations.FixedTimeEquals(legacyMaterial, migratedMaterial))
            throw new CryptographicException("Migrated agent key material failed verification.");

        // Delete only after the restricted copy exists and its bytes were verified. A failure before
        // this point leaves the legacy copy intact so the deployed agent can retry without key loss.
        File.Delete(legacyPath);
        if (File.Exists(legacyPath))
            throw new IOException("Legacy agent key material could not be removed after migration.");
        return migratedMaterial;
    }

    // Atomically create the file with 0600 from the very first byte, closing the
    // TOCTOU window that a write-then-chmod sequence would otherwise leave open.
    private static void WriteRestricted(string path, byte[] material)
    {
        EnsureKeyDirectory(Path.GetDirectoryName(path)!);

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (OperatingSystem.IsLinux())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var stream = new FileStream(path, options);
        stream.Write(material, 0, material.Length);
    }

    private static void EnsureKeyDirectory(string directory)
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // Re-assert 0700 in case the directory pre-existed with looser perms.
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string GetKeyMaterialFilePath(string fileName)
        => Path.Combine(KeyStorageRoot, fileName);

    private static string GetLegacyKeyMaterialFilePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "data", "keys", fileName);

    private static byte[] GetMachineId()
    {
        const string machineIdPath = "/etc/machine-id";
        const string dbusPath = "/var/lib/dbus/machine-id";

        if (File.Exists(machineIdPath))
            return Encoding.UTF8.GetBytes(File.ReadAllText(machineIdPath).Trim());

        if (File.Exists(dbusPath))
            return Encoding.UTF8.GetBytes(File.ReadAllText(dbusPath).Trim());

        throw new CryptographicException("Unable to derive machine key: /etc/machine-id not found.");
    }
}
