// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal sealed class BackupBundleManifest
{
    public int Version { get; set; }
    public BackupBundleFile? Database { get; set; }
    public List<BackupBundleSource> Sources { get; set; } = [];
    public List<BackupBundleDirectory> Directories { get; set; } = [];
    public List<BackupBundleFile> Files { get; set; } = [];
}

internal sealed class BackupBundleSource
{
    public string OriginalPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string EntryRoot { get; set; } = string.Empty;
}

internal sealed class BackupBundleFile
{
    public string EntryName { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long? LastWriteTimeUtcTicks { get; set; }
    public int? UnixMode { get; set; }
}

internal sealed class BackupBundleDirectory
{
    public string EntryRoot { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long? LastWriteTimeUtcTicks { get; set; }
    public int? UnixMode { get; set; }
}
