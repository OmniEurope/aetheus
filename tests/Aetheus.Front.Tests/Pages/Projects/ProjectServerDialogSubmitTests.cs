// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Projects.ProjectDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Aetheus.Front.Tests.Pages.Projects;

/// <summary>
/// Behavioural tests for ProjectServerDialog.razor.cs - OnSubmit/OnCancel assert the
/// real dialog-close payload (captured by a spy OmniDialogService), OnTypeChanged /
/// OnAgentServerSelected assert the mutated state, and the edit-mode template assertions
/// check that the type/agent selectors are actually hidden.
/// </summary>
public class ProjectServerDialogSubmitTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectServerDialogSubmitTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private List<ServerDto> MakeAgentServers() =>
    [
        new ServerDto { Id = 1, Name = "agent-01", Hostname = "10.0.0.1", Type = ServerType.Normal, Status = ServerStatus.Online, Tags = [] },
        new ServerDto { Id = 2, Name = "agent-02", Hostname = "10.0.0.2", Type = ServerType.Docker, Status = ServerStatus.Online, Tags = [] }
    ];

    /// <summary>
    /// Registers a spy OmniDialogService (lazy factory so the provider isn't built early) that
    /// records the Close payload. The dialog injects OmniDialogService, so OnSubmit/OnCancel land
    /// on the spy and we can assert exactly what was sent back to the caller.
    /// </summary>
    private void RegisterSpyDialog() =>
        Services.AddSingleton<OmniDialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<OmniDialogService>();

    private IRenderedComponent<ProjectServerDialog> RenderDialog(
        ProjectServerType type = ProjectServerType.ExternalHost,
        int? serverId = null,
        string displayName = "",
        string host = "",
        int? port = null,
        ProjectServerDto? editing = null)
    {
        return Render<ProjectServerDialog>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.ServerId, serverId)
            .Add(x => x.DisplayName, displayName)
            .Add(x => x.Host, host)
            .Add(x => x.Port, port)
            .Add(x => x.AgentServers, MakeAgentServers())
            .Add(x => x.Editing, editing));
    }

    private static void Invoke(IRenderedComponent<ProjectServerDialog> cut, string method, params object?[] args) =>
        typeof(ProjectServerDialog).GetMethod(method, Priv)!.Invoke(cut.Instance, args);

    // ── OnSubmit closes the dialog with the typed result payload ──────────────

    [Fact]
    public void OnSubmit_ExternalHost_ClosesWithExternalHostPayload()
    {
        RegisterSpyDialog();
        var cut = RenderDialog(
            type: ProjectServerType.ExternalHost,
            displayName: "My Server",
            host: "example.com",
            port: 22);

        Invoke(cut, "OnSubmit");

        Assert.True(Spy().Closed);
        var payload = Assert.IsType<ProjectServerDialogResult>(Spy().LastResult);
        Assert.Equal(ProjectServerType.ExternalHost, payload.Type);
        Assert.Equal("My Server", payload.DisplayName);
        Assert.Equal("example.com", payload.Host);
        Assert.Equal(22, payload.Port);
        Assert.Null(payload.ServerId);
    }

    [Fact]
    public void OnSubmit_AgentServer_IncludesServerId()
    {
        RegisterSpyDialog();
        var cut = RenderDialog(
            type: ProjectServerType.AgentServer,
            serverId: 1,
            displayName: "agent-01",
            host: "10.0.0.1");

        Invoke(cut, "OnSubmit");

        var payload = Assert.IsType<ProjectServerDialogResult>(Spy().LastResult);
        Assert.Equal(ProjectServerType.AgentServer, payload.Type);
        Assert.Equal(1, payload.ServerId);
    }

    [Fact]
    public void OnSubmit_WithNotes_PassesNotesInResult()
    {
        RegisterSpyDialog();
        var cut = Render<ProjectServerDialog>(p => p
            .Add(x => x.Type, ProjectServerType.ExternalHost)
            .Add(x => x.DisplayName, "with-notes")
            .Add(x => x.Host, "host.example.com")
            .Add(x => x.Notes, "Some deployment notes")
            .Add(x => x.AgentServers, MakeAgentServers()));

        Invoke(cut, "OnSubmit");

        var payload = Assert.IsType<ProjectServerDialogResult>(Spy().LastResult);
        Assert.Equal("Some deployment notes", payload.Notes);
    }

    // ── OnCancel closes the dialog with a null result ────────────────────────

    [Fact]
    public void OnCancel_ClosesDialogWithNull()
    {
        RegisterSpyDialog();
        var cut = RenderDialog();

        Invoke(cut, "OnCancel");

        Assert.True(Spy().Closed);
        Assert.Null(Spy().LastResult);
    }

    // ── OnTypeChanged ─────────────────────────────────────────────────────────

    [Fact]
    public void OnTypeChanged_ToExternalHost_ClearsServerId()
    {
        var cut = RenderDialog(type: ProjectServerType.AgentServer, serverId: 2);

        Invoke(cut, "OnTypeChanged", ProjectServerType.ExternalHost);

        Assert.Equal(ProjectServerType.ExternalHost, cut.Instance.Type);
        // Switching away from AgentServer must drop the stale server id.
        Assert.Null(cut.Instance.ServerId);
    }

    [Fact]
    public void OnTypeChanged_ToAgentServer_KeepsServerId()
    {
        var cut = RenderDialog(type: ProjectServerType.ExternalHost, serverId: 3);

        Invoke(cut, "OnTypeChanged", ProjectServerType.AgentServer);

        Assert.Equal(ProjectServerType.AgentServer, cut.Instance.Type);
        // Only the ExternalHost branch clears the id; AgentServer leaves it intact.
        Assert.Equal(3, cut.Instance.ServerId);
    }

    // ── OnAgentServerSelected ─────────────────────────────────────────────────

    [Fact]
    public void OnAgentServerSelected_ValidId_SetsHostAndDisplayName()
    {
        var cut = RenderDialog(type: ProjectServerType.AgentServer);

        Invoke(cut, "OnAgentServerSelected", (int?)1);

        Assert.Equal(1, cut.Instance.ServerId);
        Assert.Equal("10.0.0.1", cut.Instance.Host);
        // DisplayName was empty → defaulted to the server name.
        Assert.Equal("agent-01", cut.Instance.DisplayName);
    }

    [Fact]
    public void OnAgentServerSelected_ExistingDisplayName_DoesNotOverwrite()
    {
        var cut = RenderDialog(type: ProjectServerType.AgentServer, displayName: "My Custom Name");

        Invoke(cut, "OnAgentServerSelected", (int?)2);

        // Host still follows the selection…
        Assert.Equal("10.0.0.2", cut.Instance.Host);
        // …but a user-provided DisplayName is preserved.
        Assert.Equal("My Custom Name", cut.Instance.DisplayName);
    }

    [Fact]
    public void OnAgentServerSelected_NullId_SetsServerIdNull()
    {
        var cut = RenderDialog(type: ProjectServerType.AgentServer, serverId: 1);

        Invoke(cut, "OnAgentServerSelected", (int?)null);

        Assert.Null(cut.Instance.ServerId);
    }

    // ── _editMode computed flag drives the template ──────────────────────────

    [Fact]
    public void EditMode_WhenEditingIsNull_IsFalse()
    {
        var cut = RenderDialog(editing: null);
        Assert.False(EditMode(cut));
    }

    [Fact]
    public void EditMode_WhenEditingIsSet_IsTrue()
    {
        var existing = new ProjectServerDto
        {
            Id = 1,
            Type = ProjectServerType.ExternalHost,
            DisplayName = "old",
            Host = "old.host"
        };
        var cut = RenderDialog(editing: existing);
        Assert.True(EditMode(cut));
    }

    private static bool EditMode(IRenderedComponent<ProjectServerDialog> cut) =>
        (bool)typeof(ProjectServerDialog).GetProperty("_editMode", Priv)!.GetValue(cut.Instance)!;

    // ── ServerTypes enum values ────────────────────────────────────────────────

    [Fact]
    public void ServerTypes_ContainsAllEnumValues()
    {
        var cut = RenderDialog();
        var types = (ProjectServerType[])typeof(ProjectServerDialog)
            .GetField("_serverTypes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.Contains(ProjectServerType.ExternalHost, types);
        Assert.Contains(ProjectServerType.AgentServer, types);
    }

    // ── Template: edit mode hides the type/agent selector ─────────────────────

    [Fact]
    public void Render_EditMode_HidesTypeSelector_ShowsSaveAction()
    {
        var existing = new ProjectServerDto
        {
            Id = 5,
            Type = ProjectServerType.AgentServer,
            DisplayName = "locked-server",
            Host = "10.0.0.5"
        };
        var cut = RenderDialog(editing: existing);

        // The @if(!_editMode) block (type dropdown rendering the enum values) is gone…
        Assert.DoesNotContain("ExternalHost", cut.Markup);
        Assert.DoesNotContain("AgentServer", cut.Markup);
        // …and the submit button shows the edit-mode "save" icon, not "add".
        Assert.Contains(cut.FindComponents<OmniIcon>(), icon => icon.Instance.Name == OmniIconName.Save);
    }

    [Fact]
    public void Render_NewMode_ShowsTypeDropdown_AndAddAction()
    {
        var cut = RenderDialog(editing: null);

        Assert.Contains("ExternalHost", cut.Markup);
        Assert.Contains(cut.FindComponents<OmniIcon>(), icon => icon.Instance.Name == OmniIconName.Add);
    }
}
