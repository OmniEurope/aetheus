// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Projects.ProjectDetailSections;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Sweeps small pure helper methods across non-excluded pages that have no dedicated test.
/// Priority targets: RelativeTime, ServerHeartbeatHelper, ProjectServersSection badge helpers,
/// ProjectLibrariesSection navigation, ProjectEditSection OnParametersSet.
/// </summary>
public class SmallHelpersSweepTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public SmallHelpersSweepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetPaginatedJsonResponse<Aetheus.Shared.Components.Git.GitLightRepoDto>(
            HttpMethod.Get,
            "api/git/repos?page=1&pageSize=100&projectId=",
            []);
    }

    // ── RelativeTime.FormatAgo(L, TimeSpan) ──────────────────────────────────

    private static IStringLocalizer<Aetheus.Front.Resources.AppStrings> MakeLocalizer()
        => new BunitTestHelper.StubLocalizer();

    [Fact]
    public void RelativeTime_FormatAgo_NegativeSpan_TreatedAsZero_ReturnsJustNow()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.FromSeconds(-10));
        Assert.Equal("JustNow", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_ZeroSpan_ReturnsJustNow()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.Zero);
        Assert.Equal("JustNow", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_3Seconds_ReturnsJustNow()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.FromSeconds(3));
        Assert.Equal("JustNow", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_30Seconds_ReturnsSecondsFormat()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.FromSeconds(30));
        // Format is "ContactAgentSecondsAgo" localized with rounded seconds
        Assert.Contains("ContactAgentSecondsAgo", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_90Seconds_ReturnsMinutesFormat()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.FromSeconds(90));
        Assert.Contains("ContactAgentMinutesAgo", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_2Hours_ReturnsHoursFormat()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.FromHours(2));
        Assert.Contains("ContactAgentHoursAgo", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_3Days_ReturnsDaysFormat()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, TimeSpan.FromDays(3));
        Assert.Contains("DaysAgo", result);
    }

    // ── RelativeTime.FormatAgo(L, DateTime) ──────────────────────────────────

    [Fact]
    public void RelativeTime_FormatAgo_DateTime_OldYear_ReturnsNever()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, new DateTime(1999, 1, 1));
        Assert.Equal("Never", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_DateTime_RecentTime_ReturnsRelative()
    {
        var L = MakeLocalizer();
        var localTime = DateTime.Now.AddMinutes(-5);
        var result = RelativeTime.FormatAgo(L, localTime);
        // Should be some non-"Never" string (minutes format)
        Assert.NotEqual("Never", result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_DateTime_JustNow_ReturnsJustNow()
    {
        var L = MakeLocalizer();
        var result = RelativeTime.FormatAgo(L, DateTime.Now);
        Assert.Equal("JustNow", result);
    }

    [Fact]
    public void RelativeTime_FormatAgo_DateTime_DefaultValue_ReturnsNever()
    {
        var L = MakeLocalizer();
        // default(DateTime) has year 0001 < 2000
        var result = RelativeTime.FormatAgo(L, default(DateTime));
        Assert.Equal("Never", result);
    }

    // ── ServerHeartbeatHelper.Severity ───────────────────────────────────────

    [Fact]
    public void ServerHeartbeatHelper_Severity_NeverReported_ReturnsDanger()
    {
        // Year < 2000 means "never reported"
        var result = ServerHeartbeatHelper.Severity(new DateTime(1990, 1, 1));
        Assert.Equal(OmniTone.Danger, result);
    }

    [Fact]
    public void ServerHeartbeatHelper_Severity_VeryRecent_ReturnsWarning()
    {
        // Severity only grades OFFLINE badges, so a recently-lost agent must never be green:
        // a very recent heartbeat (within the danger window) is Warning, not Success.
        var recent = DateTime.Now.AddSeconds(-10);
        var result = ServerHeartbeatHelper.Severity(recent);
        Assert.Equal(OmniTone.Warning, result);
    }

    [Fact]
    public void ServerHeartbeatHelper_Severity_Stale_ReturnsWarning()
    {
        // Anything below StaleDangerSeconds (recently lost) → Warning
        var stale = DateTime.Now.AddSeconds(-(ServerHeartbeatHelper.StaleDangerSeconds - 60));
        var result = ServerHeartbeatHelper.Severity(stale);
        Assert.Equal(OmniTone.Warning, result);
    }

    [Fact]
    public void ServerHeartbeatHelper_Severity_Dead_ReturnsDanger()
    {
        // Beyond StaleDangerSeconds (300s)
        var dead = DateTime.Now.AddSeconds(-(ServerHeartbeatHelper.StaleDangerSeconds + 60));
        var result = ServerHeartbeatHelper.Severity(dead);
        Assert.Equal(OmniTone.Danger, result);
    }

    [Fact]
    public void ServerHeartbeatHelper_SeverityOmni_GradesOfflineWithoutShowingGreen()
    {
        Assert.Equal(OmniTone.Danger,
            ServerHeartbeatHelper.SeverityOmni(new DateTime(1990, 1, 1)));
        Assert.Equal(OmniTone.Warning,
            ServerHeartbeatHelper.SeverityOmni(DateTime.Now.AddSeconds(-10)));
        Assert.Equal(OmniTone.Danger,
            ServerHeartbeatHelper.SeverityOmni(DateTime.Now.AddSeconds(-(ServerHeartbeatHelper.StaleDangerSeconds + 60))));
    }

    // ── ServerHeartbeatHelper.OfflineReason ──────────────────────────────────

    [Fact]
    public void ServerHeartbeatHelper_OfflineReason_NeverReported_ReturnsNeverMessage()
    {
        var L = MakeLocalizer();
        var result = ServerHeartbeatHelper.OfflineReason(L, new DateTime(1990, 1, 1));
        Assert.Equal("ServerOfflineNeverReported", result);
    }

    [Fact]
    public void ServerHeartbeatHelper_OfflineReason_RecentHeartbeat_ContainsFormatted()
    {
        var L = MakeLocalizer();
        var recent = DateTime.Now.AddMinutes(-2);
        var result = ServerHeartbeatHelper.OfflineReason(L, recent);
        // Should use "ServerOfflineNoHeartbeat" key with relative time embedded
        Assert.Contains("ServerOfflineNoHeartbeat", result);
    }

    // ── ServerHeartbeatHelper.FormatAgo (back-compat shim) ───────────────────

    [Fact]
    public void ServerHeartbeatHelper_FormatAgo_DelegatesToRelativeTime()
    {
        var L = MakeLocalizer();
        // The shim should return the same as RelativeTime.FormatAgo
        var span = TimeSpan.FromMinutes(3);
        var viaShim = ServerHeartbeatHelper.FormatAgo(L, span);
        var viaDirect = RelativeTime.FormatAgo(L, span);
        Assert.Equal(viaShim, viaDirect);
    }

    // ── ServerHeartbeatHelper constants ──────────────────────────────────────

    [Fact]
    public void ServerHeartbeatHelper_StaleDangerSeconds_Is300()
    {
        Assert.Equal(300, ServerHeartbeatHelper.StaleDangerSeconds);
    }

    // ── ProjectServersSection.GetStatusBadge (static private) ────────────────

    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [Theory]
    [InlineData(ServerStatus.Online, OmniTone.Success)]
    [InlineData(ServerStatus.Offline, OmniTone.Danger)]
    public void ProjectServersSection_GetStatusBadge_ReturnsExpected(ServerStatus status, OmniTone expected)
    {
        var method = typeof(ProjectServersSection).GetMethod("GetStatusBadge", PrivStatic)!;
        var result = (OmniTone)method.Invoke(null, [(object?)status])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProjectServersSection_GetStatusBadge_Null_ReturnsLight()
    {
        var method = typeof(ProjectServersSection).GetMethod("GetStatusBadge", PrivStatic)!;
        var result = (OmniTone)method.Invoke(null, [(object?)null])!;
        Assert.Equal(OmniTone.Neutral, result);
    }

    [Fact]
    public void ProjectServersSection_GetStatusBadge_Disabled_ReturnsLight()
    {
        var method = typeof(ProjectServersSection).GetMethod("GetStatusBadge", PrivStatic)!;
        var result = (OmniTone)method.Invoke(null, [(object?)ServerStatus.Disabled])!;
        Assert.Equal(OmniTone.Neutral, result);
    }

    // ProjectLibrariesSection.NewLibrary moved to VariableLibrariesList (project scope) - its
    // new-library navigation is covered by VariableLibrariesListTests.

    // ── ProjectEditSection.OnParametersSet - populates _model from Project ────

    [Fact]
    public void ProjectEditSection_OnParametersSet_WithProject_PopulatesModel()
    {
        var project = new Aetheus.Shared.Components.Projects.ProjectDetailDto
        {
            Id = 5,
            Name = "Test Project",
            Description = "A test",
            RepositoryUrl = "https://git.example.com/repo.git",
            DefaultBranch = "develop",
            Status = Aetheus.Shared.Components.Projects.ProjectStatus.Active,
            Tags = ["tag1", "tag2"]
        };

        var cut = Render<ProjectEditSection>(p =>
            p.Add(x => x.Project, project));

        var modelField = typeof(ProjectEditSection).GetField("_model",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var model = modelField.GetValue(cut.Instance)!;
        var modelType = model.GetType();

        var name = (string)modelType.GetProperty("Name")!.GetValue(model)!;
        var description = (string)modelType.GetProperty("Description")!.GetValue(model)!;
        var tagsRaw = (string)modelType.GetProperty("TagsRaw")!.GetValue(model)!;

        Assert.Equal("Test Project", name);
        Assert.Equal("A test", description);
        Assert.Contains("tag1", tagsRaw);
        Assert.Contains("tag2", tagsRaw);
    }

    [Fact]
    public void ProjectEditSection_OnParametersSet_NullProject_StatusOptionsPopulated()
    {
        var cut = Render<ProjectEditSection>(p =>
            p.Add(x => x.Project, (Aetheus.Shared.Components.Projects.ProjectDetailDto?)null));

        var statusOptions = (System.Collections.IList)typeof(ProjectEditSection)
            .GetField("_statusOptions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;

        Assert.Equal(2, statusOptions.Count); // Active + Archived
    }

    // ── EnumLocalizationHelper ────────────────────────────────────────────────

    [Fact]
    public void EnumLocalizationHelper_Localize_ReturnsKeyString()
    {
        var L = MakeLocalizer();
        var result = L.Localize(PipelineTriggerType.Manual);
        // Stub localizer returns the key as the value
        Assert.Contains("PipelineTriggerType", result);
        Assert.Contains("Manual", result);
    }

    [Theory]
    [InlineData(PipelineTriggerType.Webhook)]
    [InlineData(PipelineTriggerType.Schedule)]
    [InlineData(PipelineTriggerType.Manual)]
    public void EnumLocalizationHelper_Localize_AllTriggerTypes_DoNotThrow(PipelineTriggerType type)
    {
        var L = MakeLocalizer();
        var result = L.Localize(type);
        Assert.NotEmpty(result);
    }
}
