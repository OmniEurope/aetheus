// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Alerts;
using Aetheus.Front.Pages.Audit;
using Aetheus.Front.Pages.Plugins;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.Enums;
using Radzen;
using PipelinesComp = Aetheus.Front.Helpers.PipelineHelper;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Tests for private/internal static badge/style methods across page components.
/// These are tested via reflection for private methods.
/// </summary>
public class StaticBadgeMethodsTests
{
    private static object? InvokePrivateStatic(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        return method.Invoke(null, args);
    }

    // --- Alerts.GetMetricBadgeStyle ---

    [Theory]
    [InlineData(MetricType.Cpu, BadgeStyle.Warning)]
    [InlineData(MetricType.Memory, BadgeStyle.Info)]
    [InlineData(MetricType.Disk, BadgeStyle.Danger)]
    public void Alerts_GetMetricBadgeStyle(MetricType metric, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(Alerts), "GetMetricBadgeStyle", metric);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(AlertSeverity.Critical, BadgeStyle.Danger)]
    [InlineData(AlertSeverity.Warning, BadgeStyle.Warning)]
    [InlineData(AlertSeverity.Info, BadgeStyle.Info)]
    public void Alerts_GetSeverityBadgeStyle(AlertSeverity severity, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(Alerts), "GetSeverityBadgeStyle", severity);
        Assert.Equal(expected, result);
    }

    // --- AuditLogs.GetActionBadge ---
    // Source: "Created" => Success, "Updated" => Info, "Deleted" => Danger, _ => Light

    [Theory]
    [InlineData("Created", BadgeStyle.Success)]
    [InlineData("Updated", BadgeStyle.Info)]
    [InlineData("Deleted", BadgeStyle.Danger)]
    [InlineData("Unknown", BadgeStyle.Light)]
    public void AuditLogs_GetActionBadge(string action, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(AuditLogs), "GetActionBadge", action);
        Assert.Equal(expected, result);
    }

    // --- PipelinesComp.GetTriggerIcon ---
    // Source: Webhook => "webhook", Schedule => "schedule", _ => "touch_app"

    [Theory]
    [InlineData(PipelineTriggerType.Manual, "touch_app")]
    [InlineData(PipelineTriggerType.Schedule, "schedule")]
    [InlineData(PipelineTriggerType.Webhook, "webhook")]
    public void Pipelines_GetTriggerIcon(PipelineTriggerType type, string expected)
    {
        var result = PipelinesComp.GetTriggerIcon(type);
        Assert.Equal(expected, result);
    }

    // --- PluginManagement.GetStatusBadgeStyle ---
    // Source: Enabled => Success, Disabled => Warning, Error => Danger, _ => Info

    [Theory]
    [InlineData(PluginStatus.Enabled, BadgeStyle.Success)]
    [InlineData(PluginStatus.Disabled, BadgeStyle.Warning)]
    [InlineData(PluginStatus.Error, BadgeStyle.Danger)]
    public void PluginManagement_GetStatusBadgeStyle(PluginStatus status, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(Aetheus.Front.Pages.Plugins.PluginManagement), "GetStatusBadgeStyle", status);
        Assert.Equal(expected, result);
    }

    // --- ServerDockerSection.GetContainerBadge ---

    [Theory]
    [InlineData("running", BadgeStyle.Success)]
    [InlineData("exited", BadgeStyle.Danger)]
    [InlineData("paused", BadgeStyle.Warning)]
    [InlineData("restarting", BadgeStyle.Info)]
    [InlineData("created", BadgeStyle.Light)]
    public void ServerDockerSection_GetContainerBadge(string state, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(DockerContainersTab), "GetContainerBadge", state);
        Assert.Equal(expected, result);
    }

    // --- DockerContainersTab.GetProjectAccentClass ---
    // Source: null => docker-project-accent-none, non-null => $"docker-project-accent-{hash % 8}"

    [Fact]
    public void ServerDockerSection_GetProjectAccentClass_Null()
    {
        var result = DockerContainersTab.GetProjectAccentClass(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ServerDockerSection_GetProjectAccentClass_NonNull()
    {
        var result = DockerContainersTab.GetProjectAccentClass("my-project");
        Assert.StartsWith("docker-project-accent-", result);
    }

    // --- ServerRkhunterSection.GetScanBadgeStyle ---
    // Source: "clean" => Success, "warning" => Warning, _ => Secondary

    [Theory]
    [InlineData("clean", BadgeStyle.Success)]
    [InlineData("warning", BadgeStyle.Warning)]
    [InlineData("error", BadgeStyle.Light)]
    public void ServerRkhunterSection_GetScanBadgeStyle(string status, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ServerRkhunterSection), "GetScanBadgeStyle", status);
        Assert.Equal(expected, result);
    }

    // --- ServerModulesSection.GetModuleStatusBadge ---

    [Theory]
    [InlineData(ServerModuleStatus.Active, BadgeStyle.Success)]
    [InlineData(ServerModuleStatus.Inactive, BadgeStyle.Light)]
    [InlineData(ServerModuleStatus.Error, BadgeStyle.Danger)]
    public void ServerModulesSection_GetModuleStatusBadge(ServerModuleStatus status, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ServerModulesSection), "GetModuleStatusBadge", status);
        Assert.Equal(expected, result);
    }

    // --- ServerServicesSection.FormatType ---
    // Source: Systemd => "Systemd", Docker => "Docker", WindowsService => "Windows", _ => type.ToString()

    [Theory]
    [InlineData(ServiceType.Systemd, "Systemd")]
    [InlineData(ServiceType.Docker, "Docker")]
    [InlineData(ServiceType.WindowsService, "Windows")]
    public void ServerServicesSection_FormatType(ServiceType type, string expected)
    {
        var result = ServerServicesSection.FormatType(type);
        Assert.Equal(expected, result);
    }

    // --- ServerServicesSection.HasDedicatedModule ---

    [Theory]
    [InlineData("apache2", true)]
    [InlineData("postfix", true)]
    [InlineData("dovecot", true)]
    [InlineData("nginx", false)]
    [InlineData("sshd", false)]
    public void ServerServicesSection_HasDedicatedModule(string serviceName, bool expected)
    {
        Assert.Equal(expected, ServerServicesSection.HasDedicatedModule(serviceName));
    }

    // --- ServerConfigSection.GetChangeBadgeStyle ---
    // Source: "pull"|"create"|"deploy"|"enable" => Success, "update" => Info, "unchanged" => Light, _ => Secondary

    [Theory]
    [InlineData("create", BadgeStyle.Success)]
    [InlineData("pull", BadgeStyle.Success)]
    [InlineData("deploy", BadgeStyle.Success)]
    [InlineData("enable", BadgeStyle.Success)]
    [InlineData("update", BadgeStyle.Info)]
    [InlineData("unchanged", BadgeStyle.Light)]
    [InlineData("other", BadgeStyle.Light)]
    public void ServerConfigSection_GetChangeBadgeStyle(string action, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ServerConfigSection), "GetChangeBadgeStyle", action);
        Assert.Equal(expected, result);
    }

    // --- ServerAppsSection.GetAppStatusBadge ---
    // Source: Running => Success, Stopped => Light, Error => Danger, _ => Warning

    [Theory]
    [InlineData(ServerAppStatus.Running, BadgeStyle.Success)]
    [InlineData(ServerAppStatus.Stopped, BadgeStyle.Light)]
    [InlineData(ServerAppStatus.Error, BadgeStyle.Danger)]
    public void ServerAppsSection_GetAppStatusBadge(ServerAppStatus status, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ServerAppsSection), "GetAppStatusBadge", status);
        Assert.Equal(expected, result);
    }

    // --- ServerApacheSection.GetStatusBadge ---

    [Theory]
    [InlineData(true, BadgeStyle.Success)]
    [InlineData(false, BadgeStyle.Danger)]
    public void ServerApacheSection_GetStatusBadge(bool isRunning, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ServerApacheSection), "GetStatusBadge", isRunning);
        Assert.Equal(expected, result);
    }

    // --- ModuleLinksTab.GetTypeBadge ---
    // Source: Docker => Primary, Apache => Warning, Certbot => Success, _ => Light

    [Theory]
    [InlineData(ModuleLinkType.Docker, BadgeStyle.Primary)]
    [InlineData(ModuleLinkType.Apache, BadgeStyle.Warning)]
    [InlineData(ModuleLinkType.Certbot, BadgeStyle.Success)]
    public void ModuleLinksTab_GetTypeBadge(ModuleLinkType type, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ModuleLinksTab), "GetTypeBadge", type);
        Assert.Equal(expected, result);
    }

    // --- ServerPortsentrySection.GetBlockedCountBadgeStyle ---
    // Source: 0 => Success, <= 5 => Warning, _ => Danger

    [Theory]
    [InlineData(0, BadgeStyle.Success)]
    [InlineData(3, BadgeStyle.Warning)]
    [InlineData(5, BadgeStyle.Warning)]
    [InlineData(6, BadgeStyle.Danger)]
    [InlineData(50, BadgeStyle.Danger)]
    public void ServerPortsentrySection_GetBlockedCountBadgeStyle(int count, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(ServerPortsentrySection), "GetBlockedCountBadgeStyle", count);
        Assert.Equal(expected, result);
    }

    // --- UserEdit.GetPermissionBadgeStyle ---
    // Source: Admin => Danger, Write => Warning, _ => Info

    [Theory]
    [InlineData(Permission.Read, BadgeStyle.Info)]
    [InlineData(Permission.Write, BadgeStyle.Warning)]
    [InlineData(Permission.Admin, BadgeStyle.Danger)]
    public void UserEdit_GetPermissionBadgeStyle(Permission perm, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(Aetheus.Front.Pages.Users.UserEdit), "GetPermissionBadgeStyle", perm);
        Assert.Equal(expected, result);
    }

    // --- ProjectServersSection.GetTypeBadge ---
    // Source: AgentServer => Info, ExternalHost => Warning, _ => Light

    [Theory]
    [InlineData(ProjectServerType.AgentServer, BadgeStyle.Info)]
    [InlineData(ProjectServerType.ExternalHost, BadgeStyle.Warning)]
    public void ProjectServersSection_GetTypeBadge(ProjectServerType type, BadgeStyle expected)
    {
        var result = InvokePrivateStatic(typeof(Aetheus.Front.Pages.Projects.ProjectDetailSections.ProjectServersSection), "GetTypeBadge", type);
        Assert.Equal(expected, result);
    }
}
