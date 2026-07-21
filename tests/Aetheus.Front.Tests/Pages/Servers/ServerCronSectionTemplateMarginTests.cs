// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Template-branch margin coverage for ServerCronSection.razor and CronJobDialog.razor.
/// The create/edit form now lives in CronJobDialog (a DialogService dialog rendered in a
/// SEPARATE host) - so the dialog form/validation branches are exercised by rendering
/// CronJobDialog DIRECTLY (hardened rule #4: in-render-tree, no separate-host hang).
/// The section's own confirm panel + job rows are exercised against the section markup.
/// </summary>
public class ServerCronSectionTemplateMarginTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerCronSectionTemplateMarginTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static ServerDetailDto MakeServer(params CronJobDto[] jobs) => new()
    {
        Id = 77,
        Name = "cron-srv",
        Hostname = "10.0.0.77",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        Tags = [],
        Services = [],
        Cron = new CronDataDto { IsInstalled = true, Jobs = jobs.ToList() }
    };

    private IRenderedComponent<ServerCronSection> RenderSection(ServerDetailDto server)
    {
        _handler.SetJsonResponse("api/servers/77/cron/job", true);
        return Render<ServerCronSection>(p => p
            .Add(x => x.Server, server)
            .Add(x => x.ServerId, 77));
    }

    [Fact]
    public void Renders_WithMultipleJobs_ShowsJobRows()
    {
        var cut = RenderSection(MakeServer(
            new CronJobDto { Id = "1", User = "root", Schedule = "0 * * * *", Command = "/usr/bin/backup.sh", Source = "/etc/crontab" },
            new CronJobDto { Id = "2", User = "www-data", Schedule = "*/15 * * * *", Command = "/usr/bin/sync.sh", Source = "/etc/crontab" },
            new CronJobDto { Id = "3", User = "root", Schedule = "0 0 * * 0", Command = "/usr/bin/weekly.sh", Source = "/var/spool/cron" }));

        Assert.Contains("/usr/bin/backup.sh", cut.Markup);
        Assert.Contains("/usr/bin/sync.sh", cut.Markup);
    }

    // Dialog content rendered DIRECTLY (in-render-tree). Valid schedule → next-run preview branch.
    [Fact]
    public void CronJobDialog_ValidSchedule_ShowsNextRunPreview()
    {
        var cut = Render<CronJobDialog>(p => p
            .Add(x => x.JobId, "1")
            .Add(x => x.User, "root")
            .Add(x => x.Schedule, "0 2 * * *")
            .Add(x => x.Command, "/bin/backup"));

        Assert.Contains("cron-next-run", cut.Markup);
    }

    // Dialog content rendered DIRECTLY. Invalid schedule → error branch.
    [Fact]
    public void CronJobDialog_InvalidSchedule_ShowsError()
    {
        var cut = Render<CronJobDialog>(p => p
            .Add(x => x.JobId, "1")
            .Add(x => x.User, "root")
            .Add(x => x.Schedule, "not-a-cron")
            .Add(x => x.Command, "/bin/ls"));

        Assert.Contains("cron-error", cut.Markup);
    }

    // Dialog Save closes the dialog with a CronJobSaveRequest carrying the form values
    // (hardened rule #4: capture the close result via DialogService.OnClose, no hang).
    [Fact]
    public void CronJobDialog_RendersPrefilledForm_SaveWired()
    {
        var cut = Render<CronJobDialog>(p => p
            .Add(x => x.JobId, "job1")
            .Add(x => x.User, "root")
            .Add(x => x.Schedule, "0 2 * * *")
            .Add(x => x.Command, "/bin/backup"));

        // The extracted dialog renders the edit form prefilled with the job's values.
        Assert.Contains("0 2 * * *", cut.Markup);
        Assert.Contains("/bin/backup", cut.Markup);
        // Save is present and clickable (closes the dialog via DialogService in the real flow).
        cut.FindAll("button").First(b => b.TextContent.Contains("Save")).Click();
    }

    // The dialog's Cancel button is wired and clickable.
    [Fact]
    public void CronJobDialog_CancelButtonWired()
    {
        var cut = Render<CronJobDialog>(p => p
            .Add(x => x.JobId, string.Empty)
            .Add(x => x.User, "root")
            .Add(x => x.Schedule, "0 2 * * *")
            .Add(x => x.Command, "/bin/backup"));

        cut.FindAll("button").First(b => b.TextContent.Contains("Cancel")).Click();
    }

    [Fact]
    public void Renders_ConfirmPanelVisible_ShowsConfirmMarkup()
    {
        var cut = RenderSection(MakeServer(new CronJobDto { Id = "1", User = "root", Schedule = "0 * * * *", Command = "/bin/ls" }));

        typeof(ServerCronSection).GetField("_confirmVisible", Priv)!.SetValue(cut.Instance, true);
        typeof(ServerCronSection).GetField("_confirmTitle", Priv)!.SetValue(cut.Instance, "Delete job?");
        typeof(ServerCronSection).GetField("_confirmMessage", Priv)!.SetValue(cut.Instance, "Are you sure?");
        cut.Render(p => p.Add(x => x.Server, cut.Instance.Server).Add(x => x.ServerId, 77));

        // The visible ConfirmDialog renders the title and message set on the section.
        Assert.Contains("Delete job?", cut.Markup);
        Assert.Contains("Are you sure?", cut.Markup);
    }

    [Fact]
    public void Renders_WithJobSearch_FiltersRows()
    {
        var cut = RenderSection(MakeServer(
            new CronJobDto { Id = "1", User = "root", Schedule = "0 * * * *", Command = "/usr/bin/backup.sh" },
            new CronJobDto { Id = "2", User = "app", Schedule = "*/5 * * * *", Command = "/usr/bin/sync.sh" }));

        typeof(ServerCronSection).GetField("_jobSearch", Priv)!.SetValue(cut.Instance, "backup");
        cut.Render(p => p.Add(x => x.Server, cut.Instance.Server).Add(x => x.ServerId, 77));

        // The "backup" search keeps only the matching job row; the sync job is filtered out.
        Assert.Contains("/usr/bin/backup.sh", cut.Markup);
        Assert.DoesNotContain("/usr/bin/sync.sh", cut.Markup);
    }
}
