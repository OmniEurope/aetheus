// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal sealed class VerifiedBackupBundle(
    string extractionRoot,
    string? databaseDumpPath,
    IReadOnlyList<BackupBundleSource> sources) : IDisposable
{
    internal string ExtractionRoot { get; } = extractionRoot;
    internal string? DatabaseDumpPath { get; } = databaseDumpPath;
    internal IReadOnlyList<BackupBundleSource> Sources { get; } = sources;

    public void Dispose()
    {
        try { if (Directory.Exists(ExtractionRoot)) Directory.Delete(ExtractionRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
