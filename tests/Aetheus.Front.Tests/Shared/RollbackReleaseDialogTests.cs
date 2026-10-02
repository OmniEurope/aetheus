// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public sealed class RollbackReleaseDialogTests : BunitContext
{
    [Fact]
    public void BackupRunFailure_IsContainedWithoutDiscardingOtherVerifiedBackups()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetPaginatedJsonResponse("api/backups", new List<BackupPolicyDto>
        {
            new() { Id = 1, ProjectId = 7, Name = "database" },
            new() { Id = 2, ProjectId = 7, Name = "files" }
        });
        handler.SetPaginatedJsonResponse("api/backups/1/runs", new List<BackupRunDto>
        {
            new()
            {
                Id = 10,
                BackupPolicyId = 1,
                Status = BackupRunStatus.Succeeded,
                RestoreCheckStatus = RestoreCheckStatus.Verified,
                StartedAt = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc)
            }
        });
        handler.SetResponse("api/backups/2/runs", HttpStatusCode.ServiceUnavailable);

        var cut = Render<RollbackReleaseDialog>(parameters => parameters
            .Add(component => component.ProjectId, 7)
            .Add(component => component.Pipelines, [new PipelineDto { Id = 3, Name = "rollback" }]));

        Assert.Contains("RestoreDatabaseFromVerifiedBackup", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count(request => request.Url.Contains("/runs", StringComparison.Ordinal)));
    }
}
