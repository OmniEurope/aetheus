// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>The database engine a backup policy dumps (PLAN-006 4.3). <c>None</c> = files-only backup.</summary>
public enum BackupDbEngine
{
    None = 0,
    Postgres = 1,
    MySql = 2
}

/// <summary>Lifecycle of a single backup execution (PLAN-006 4.3).</summary>
public enum BackupRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>
/// The no-fake recoverability state of a backup (PLAN-006 4.3). A backup starts <c>Unverified</c> and only
/// becomes <c>Verified</c> after a restore-check actually succeeds on a throwaway target; a failed
/// restore-check is <c>Failed</c> (red, visible), never silently "ok".
/// </summary>
public enum RestoreCheckStatus
{
    Unverified = 0,
    Verified = 1,
    Failed = 2
}
