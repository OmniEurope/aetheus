// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace Aetheus.Back.Extensions;

/// <summary>
/// Maps the two anonymous endpoints serving the Linux/Windows agent archives, and provides
/// helpers to build / re-pack them with a server-URL marker.
/// Lives outside <c>Program.cs</c> to keep host wiring readable.
/// </summary>
internal static class AgentDownloadsExtensions
{
    private sealed record PreparedArchive(string Path, string Sha256);
    private static readonly ConcurrentDictionary<string, Lazy<Task<PreparedArchive>>> PreparedLinuxArchives = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> FileHashes = new();

    public static WebApplication MapAgentDownloads(this WebApplication app)
    {
        var downloadsPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "downloads");
        Directory.CreateDirectory(downloadsPath);

        var rawAppVersion = app.Configuration["App:Version"] ?? "dev";
        var safeAppVersion = Regex.Replace(rawAppVersion, "[^a-zA-Z0-9._-]", "-");

        EnsureAgentDownloadPackages(app.Environment.ContentRootPath, downloadsPath);

        app.MapGet("/downloads/aetheus-agent-linux-x64.tar.gz", async (HttpContext httpContext) =>
        {
            httpContext.Response.ContentType = "application/gzip";
            httpContext.Response.Headers[HeaderNames.ContentDisposition] =
                $"attachment; filename=\"aetheus-agent-linux-x64-v{safeAppVersion}.tar.gz\"";

            // Resolve the public API URL from configuration - never derive from Request.Host.
            // Request.Host may point to the frontend hostname when the user downloads through
            // the front; baking that value into .aetheus-server-url would make the agent
            // POST /api/auth/register on the frontend and hit 405 Method Not Allowed.
            var serverUrl = HostUrlExtensions.ResolvePublicApiUrl(app.Configuration, httpContext);

            // Freshness guard: the dev archive is rebuilt from the linux-x64 publish output, which is
            // refreshed by `dotnet publish -r linux-x64` (ylaunch does this after a build) - NOT by a
            // plain `dotnet build`. If that publish silently lagged the latest agent build, an agent
            // self-update/install would pull stale code. Warn loudly so it's caught before deploy.
            WarnIfLinuxAgentPublishStale(app);

            // Serve pre-built archive if present (Docker / production).
            // The Dockerfile creates a versioned file (e.g. aetheus-agent-linux-x64-v1.0.0.tar.gz).
            var prebuiltArchive = Directory.GetFiles(downloadsPath, "aetheus-agent-linux-x64*.tar.gz")
                .OrderByDescending(f => f)
                .FirstOrDefault();

            // Both paths re-pack to a temp file so .aetheus-server-url reflects the configured
            // API URL - which also lets us hash the exact payload before streaming it (F-004:
            // X-Content-SHA256 is verified fail-closed by the agent self-update).
            var solutionRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", ".."));
            var installScript = Path.Combine(solutionRoot, "deploy", "scripts", "install-agent-linux.sh");

            if (prebuiltArchive is not null)
            {
                var prepared = await GetPreparedLinuxArchiveAsync(prebuiltArchive, serverUrl, installScript);
                httpContext.Response.Headers["X-Content-SHA256"] = prepared.Sha256;
                await using var preparedStream = new FileStream(
                    prepared.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                httpContext.Response.ContentLength = preparedStream.Length;
                await preparedStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
                return;
            }

            var tempArchivePath = await TryBuildLinuxAgentArchiveAsync(app.Environment.ContentRootPath, serverUrl);

            if (tempArchivePath is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            try
            {
                httpContext.Response.Headers["X-Content-SHA256"] = await ComputeFileSha256Async(tempArchivePath);
                await using var archiveStream = File.OpenRead(tempArchivePath);
                httpContext.Response.ContentLength = archiveStream.Length;
                await archiveStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
            finally
            {
                if (File.Exists(tempArchivePath))
                    File.Delete(tempArchivePath);
            }
        }).AllowAnonymous();

        app.MapGet("/downloads/aetheus-agent-win-x64.zip", async (HttpContext httpContext) =>
        {
            httpContext.Response.ContentType = "application/zip";
            httpContext.Response.Headers[HeaderNames.ContentDisposition] =
                $"attachment; filename=\"aetheus-agent-win-x64-v{safeAppVersion}.zip\"";

            var prebuiltArchive = Directory.GetFiles(downloadsPath, "aetheus-agent-win-x64*.zip")
                .OrderByDescending(f => f)
                .FirstOrDefault();

            if (prebuiltArchive != null)
            {
                httpContext.Response.Headers["X-Content-SHA256"] = await GetCachedFileSha256Async(prebuiltArchive);
                await using var fs = new FileStream(
                    prebuiltArchive, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                httpContext.Response.ContentLength = fs.Length;
                await fs.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        }).AllowAnonymous();

        var downloadsContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        downloadsContentTypeProvider.Mappings[".tar.gz"] = "application/gzip";
        downloadsContentTypeProvider.Mappings[".tgz"] = "application/gzip";
        downloadsContentTypeProvider.Mappings[".msi"] = "application/x-msi";
        downloadsContentTypeProvider.Mappings[".sh"] = "application/x-sh";
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(downloadsPath),
            RequestPath = "/downloads",
            ContentTypeProvider = downloadsContentTypeProvider,
            ServeUnknownFileTypes = false,
        });

        return app;
    }

    private static async Task<PreparedArchive> GetPreparedLinuxArchiveAsync(
        string prebuiltArchive, string serverUrl, string? freshInstallScript)
    {
        var archive = new FileInfo(prebuiltArchive);
        var scriptStamp = freshInstallScript is not null && File.Exists(freshInstallScript)
            ? File.GetLastWriteTimeUtc(freshInstallScript).Ticks
            : 0;
        var key = string.Join('|', archive.FullName, archive.Length, archive.LastWriteTimeUtc.Ticks, scriptStamp, serverUrl);
        var lazy = PreparedLinuxArchives.GetOrAdd(
            key,
            _ => new Lazy<Task<PreparedArchive>>(
                async () =>
                {
                    var path = await BuildPrebuiltLinuxArchiveWithMarkerAsync(
                        prebuiltArchive, serverUrl, freshInstallScript).ConfigureAwait(false);
                    return new PreparedArchive(path, await ComputeFileSha256Async(path).ConfigureAwait(false));
                },
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var prepared = await lazy.Value.ConfigureAwait(false);
            if (File.Exists(prepared.Path)) return prepared;

            PreparedLinuxArchives.TryRemove(key, out _);
            throw new IOException("Prepared Linux agent archive disappeared before it could be served.");
        }
        catch
        {
            PreparedLinuxArchives.TryRemove(key, out _);
            throw;
        }
    }

    private static readonly Lock _packageLock = new();

    private static async Task<string> GetCachedFileSha256Async(string path)
    {
        var file = new FileInfo(path);
        var key = string.Join('|', file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
        var lazy = FileHashes.GetOrAdd(
            key,
            _ => new Lazy<Task<string>>(
                () => ComputeFileSha256Async(path),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            FileHashes.TryRemove(key, out _);
            throw;
        }
    }

    private static void EnsureAgentDownloadPackages(string backContentRootPath, string downloadsPath)
    {
        var solutionRootPath = Path.GetFullPath(Path.Combine(backContentRootPath, "..", ".."));
        var scriptsPath = Path.Combine(solutionRootPath, "deploy", "scripts");

        // Windows agent: build from local Debug output only if no pre-built archive exists.
        // In Docker/production, the Dockerfile already created the versioned zip - skip rebuilding.
        // Lock: parallel WebApplicationFactory instances in integration tests race here.
        lock (_packageLock)
        {
            var existingWinZip = Directory.GetFiles(downloadsPath, "aetheus-agent-win-x64*.zip").FirstOrDefault();
            if (existingWinZip == null)
            {
                var windowsBuildPath = Path.Combine(solutionRootPath, "src", "Aetheus.Agent.Windows", "bin", "Debug", "net10.0-windows");
                var windowsInstallScriptPath = Path.Combine(scriptsPath, "install-agent-windows.ps1");

                if (Directory.Exists(windowsBuildPath) && File.Exists(windowsInstallScriptPath))
                {
                    var windowsPackageTempPath = Path.Combine(downloadsPath, "_windows_pkg_tmp");
                    RecreateDirectory(windowsPackageTempPath);
                    CopyDirectoryContent(windowsBuildPath, windowsPackageTempPath);
                    File.Copy(windowsInstallScriptPath, Path.Combine(windowsPackageTempPath, "install-agent-windows.ps1"), true);
                    var windowsArchivePath = Path.Combine(downloadsPath, "aetheus-agent-win-x64.zip");
                    ZipFile.CreateFromDirectory(windowsPackageTempPath, windowsArchivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
                    Directory.Delete(windowsPackageTempPath, true);
                }
            }
        }

        // Linux agent: pre-built versioned archive (Docker) is served by the MapGet endpoint on demand.
        // In dev mode, the endpoint builds dynamically from Debug output when requested.
    }

    /// <summary>Builds the dev-mode Linux archive from Debug output into a temp file the caller owns (and deletes).</summary>
    private static async Task<string?> TryBuildLinuxAgentArchiveAsync(string backContentRootPath, string serverUrl)
    {
        var solutionRootPath = Path.GetFullPath(Path.Combine(backContentRootPath, "..", ".."));
        // Prefer cross-compiled linux-x64 publish output (produced by ylaunch -ra).
        var linuxPublishPath = Path.Combine(solutionRootPath, "src", "Aetheus.Agent.Linux", "bin", "Debug", "net10.0", "linux-x64", "publish");
        var linuxBuildPath = Directory.Exists(linuxPublishPath)
            ? linuxPublishPath
            : Path.Combine(solutionRootPath, "src", "Aetheus.Agent.Linux", "bin", "Debug", "net10.0");
        var linuxInstallScriptPath = Path.Combine(solutionRootPath, "deploy", "scripts", "install-agent-linux.sh");

        if (!Directory.Exists(linuxBuildPath) || !File.Exists(linuxInstallScriptPath))
            return null;

        var linuxPackageTempPath = Path.Combine(Path.GetTempPath(), $"aetheus-linux-pkg-{Guid.NewGuid():N}");
        RecreateDirectory(linuxPackageTempPath);

        try
        {
            CopyDirectoryContent(linuxBuildPath, linuxPackageTempPath);
            // Normalize shell script to LF - File.Copy preserves CRLF from Windows checkout.
            var installScriptDest = Path.Combine(linuxPackageTempPath, "install-agent-linux.sh");
            File.Copy(linuxInstallScriptPath, installScriptDest, true);
            NormalizeToLf(installScriptDest);
            File.WriteAllText(Path.Combine(linuxPackageTempPath, ".aetheus-server-url"), serverUrl + "\n");

            var tempArchivePath = Path.Combine(Path.GetTempPath(), $"aetheus-linux-{Guid.NewGuid():N}.tar.gz");
            await using (var fs = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var gzipStream = new GZipStream(fs, CompressionLevel.Optimal))
            {
                await TarFile.CreateFromDirectoryAsync(linuxPackageTempPath, gzipStream, includeBaseDirectory: false).ConfigureAwait(false);
            }

            return tempArchivePath;
        }
        finally
        {
            if (Directory.Exists(linuxPackageTempPath))
                Directory.Delete(linuxPackageTempPath, true);
        }
    }

    /// <summary>Re-packs the prebuilt Linux archive with the server-URL marker and the
    /// current install script into a temp file the caller owns (and deletes).</summary>
    private static async Task<string> BuildPrebuiltLinuxArchiveWithMarkerAsync(string prebuiltArchive, string serverUrl, string? freshInstallScript)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), $"aetheus-linux-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        try
        {
            await using (var fs = File.OpenRead(prebuiltArchive))
            await using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                await TarFile.ExtractToDirectoryAsync(gz, extractDir, overwriteFiles: true).ConfigureAwait(false);
            }

            await File.WriteAllTextAsync(
                Path.Combine(extractDir, ".aetheus-server-url"),
                serverUrl + "\n").ConfigureAwait(false);

            // Overlay the current install script so prebuilt archives
            // pick up flag additions (--module, --allow-insecure-certs, etc.).
            if (freshInstallScript is not null && File.Exists(freshInstallScript))
            {
                var dest = Path.Combine(extractDir, "install-agent-linux.sh");
                File.Copy(freshInstallScript, dest, true);
                NormalizeToLf(dest);
            }

            var tempArchivePath = Path.Combine(Path.GetTempPath(), $"aetheus-linux-{Guid.NewGuid():N}.tar.gz");
            await using (var fs = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var gzipStream = new GZipStream(fs, CompressionLevel.Optimal))
            {
                await TarFile.CreateFromDirectoryAsync(extractDir, gzipStream, includeBaseDirectory: false).ConfigureAwait(false);
            }

            return tempArchivePath;
        }
        finally
        {
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
        }
    }

    /// <summary>
    /// Logs a loud warning when the linux-x64 publish output the dev archive is built from is older
    /// than the latest agent build - i.e. someone built but the publish silently lagged, so a
    /// downloading agent would get stale code. No-op in Docker/prod (paths absent) and when fresh.
    /// </summary>
    private static void WarnIfLinuxAgentPublishStale(WebApplication app)
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", ".."));
        var publishDll = Path.Combine(solutionRoot, "src", "Aetheus.Agent.Linux", "bin", "Debug", "net10.0", "linux-x64", "publish", "Aetheus.Agent.Core.dll");
        var buildDll = Path.Combine(solutionRoot, "src", "Aetheus.Agent.Core", "bin", "Debug", "net10.0", "Aetheus.Agent.Core.dll");
        if (!File.Exists(publishDll) || !File.Exists(buildDll)) return;

        var publishTime = File.GetLastWriteTimeUtc(publishDll);
        var buildTime = File.GetLastWriteTimeUtc(buildDll);
        // 5s slack absorbs build/publish ordering jitter.
        if (publishTime < buildTime.AddSeconds(-5))
        {
            app.Logger.LogWarning(
                "Served linux agent publish is STALE: published {PublishTime:u} but the agent was last built {BuildTime:u}. " +
                "A downloading agent will get OLDER code - re-run the linux-x64 publish (ylaunch does this after a build).",
                publishTime, buildTime);
        }
    }

    private static async Task<string> ComputeFileSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);

        Directory.CreateDirectory(path);
    }

    private static void NormalizeToLf(string filePath)
    {
        var content = File.ReadAllText(filePath);
        File.WriteAllText(filePath, content.Replace("\r\n", "\n").Replace("\r", "\n"));
    }

    private static void CopyDirectoryContent(string sourcePath, string destinationPath)
    {
        foreach (var sourceFilePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFilePath);
            var destinationFilePath = Path.Combine(destinationPath, relativePath);
            var destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destinationDirectoryPath))
                Directory.CreateDirectory(destinationDirectoryPath);

            File.Copy(sourceFilePath, destinationFilePath, true);
        }
    }
}

internal static class HostUrlExtensions
{
    public static string ResolvePublicApiUrl(IConfiguration config, HttpContext httpContext)
    {
        // Configured value wins - deployments should set Aetheus:PublicApiBaseUrl in the
        // backend appsettings to the canonical API URL (e.g. https://aetheus-api.example.com).
        var configured = config["Aetheus:PublicApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        // Dev fallback only: use the incoming request host.
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    }
}
