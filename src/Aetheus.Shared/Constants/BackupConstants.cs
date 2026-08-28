// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>Env-var contract for the backup operations (ADR-024 4.3). The DB password rides in a
/// protected (encrypted) env var, off the process list, never interpolated into a shell.</summary>
public static class BackupConstants
{
    public const string EngineEnvVar = "AETHEUS_BACKUP_ENGINE";        // "Postgres" | "MySql" | "None"
    public const string DbHostEnvVar = "AETHEUS_BACKUP_DB_HOST";
    public const string DbPortEnvVar = "AETHEUS_BACKUP_DB_PORT";
    public const string DbNameEnvVar = "AETHEUS_BACKUP_DB_NAME";
    public const string DbUserEnvVar = "AETHEUS_BACKUP_DB_USER";
    public const string DbPasswordEnvVar = "AETHEUS_BACKUP_DB_PASSWORD";
    public const string FilePathsEnvVar = "AETHEUS_BACKUP_FILE_PATHS";  // JSON array
    public const string PolicyIdEnvVar = "AETHEUS_BACKUP_POLICY_ID";
    public const string RunIdEnvVar = "AETHEUS_BACKUP_RUN_ID";
    public const string RetentionEnvVar = "AETHEUS_BACKUP_RETENTION";
    public const string ArchivePathEnvVar = "AETHEUS_BACKUP_ARCHIVE_PATH"; // restore-check input

    /// <summary>Base dir where the agent keeps backup archives on the target host (retained locally,
    /// not shipped to the control plane - large dumps must not transit the API).</summary>
    public const string BaseDir = "/var/lib/aetheus-agent/backups";
}
