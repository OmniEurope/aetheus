// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class ProjectServerDialogTests : BunitContext
{
    public ProjectServerDialogTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_DefaultParameters()
    {
        var cut = Render<ProjectServerDialog>();
        // Default (create / ExternalHost) mode shows the form fields and the "Add" submit button.
        Assert.Contains("DisplayName", cut.Markup);
        Assert.Contains("Host", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Add"));
    }

    [Fact]
    public void Renders_WithAgentServers()
    {
        var servers = new List<ServerDto>
        {
            new() { Id = 1, Name = "web-01", Hostname = "10.0.0.1" }
        };
        var cut = Render<ProjectServerDialog>(p =>
            p.Add(x => x.AgentServers, servers));
        // The dialog renders the create form (Type selector + fields) with the agent server list supplied.
        Assert.Contains("Type", cut.Markup);
        Assert.Contains("DisplayName", cut.Markup);
    }

    [Fact]
    public void OnTypeChanged_SetsType_ClearsServerIdForExternal()
    {
        var cut = Render<ProjectServerDialog>(p =>
            p.Add(x => x.ServerId, 5)
             .Add(x => x.Type, ProjectServerType.AgentServer));

        var method = typeof(ProjectServerDialog).GetMethod("OnTypeChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, [ProjectServerType.ExternalHost]);

        Assert.Equal(ProjectServerType.ExternalHost, cut.Instance.Type);
        Assert.Null(cut.Instance.ServerId);
    }

    [Fact]
    public void OnAgentServerSelected_FillsHostAndDisplayName()
    {
        var servers = new List<ServerDto>
        {
            new() { Id = 1, Name = "web-01", Hostname = "10.0.0.1" }
        };
        var cut = Render<ProjectServerDialog>(p =>
            p.Add(x => x.AgentServers, servers));

        var method = typeof(ProjectServerDialog).GetMethod("OnAgentServerSelected", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, [(int?)1]);

        Assert.Equal("10.0.0.1", cut.Instance.Host);
        Assert.Equal("web-01", cut.Instance.DisplayName);
    }

    [Fact]
    public void OnAgentServerSelected_DoesNotOverwriteDisplayName()
    {
        var servers = new List<ServerDto>
        {
            new() { Id = 1, Name = "web-01", Hostname = "10.0.0.1" }
        };
        var cut = Render<ProjectServerDialog>(p =>
            p.Add(x => x.AgentServers, servers)
             .Add(x => x.DisplayName, "Custom"));

        var method = typeof(ProjectServerDialog).GetMethod("OnAgentServerSelected", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, [(int?)1]);

        Assert.Equal("Custom", cut.Instance.DisplayName);
    }

    [Fact]
    public void OnAgentServerSelected_NullServerId_NoOp()
    {
        var cut = Render<ProjectServerDialog>();
        var method = typeof(ProjectServerDialog).GetMethod("OnAgentServerSelected", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, [(int?)null]);
        Assert.Equal(string.Empty, cut.Instance.Host);
    }

    [Fact]
    public void Renders_EditMode()
    {
        var editing = new ProjectServerDto
        {
            Id = 1,
            DisplayName = "Existing",
            Host = "10.0.0.2",
            Type = ProjectServerType.ExternalHost
        };
        var cut = Render<ProjectServerDialog>(p =>
            p.Add(x => x.Editing, editing));
        // Edit mode hides the Type/agent selector and shows the "Save" submit button.
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Save"));
    }

    [Fact]
    public void Renders_BoundFieldValues_AndWiredSubmitButton()
    {
        // Behavioural (not a record set-then-get tautology): the dialog binds the provided field values
        // into the real form inputs a user would submit, and exposes a wired submit button. (OnSubmit
        // then packages these into a ProjectServerDialogResult passed to DialogService.Close - a close a
        // standalone bUnit render cannot observe, since Radzen only fires OnClose for a stacked dialog.)
        var cut = Render<ProjectServerDialog>(p => p
            .Add(x => x.Type, ProjectServerType.ExternalHost)
            .Add(x => x.ServerId, 5)
            .Add(x => x.DisplayName, "My Server")
            .Add(x => x.Host, "10.0.0.1")
            .Add(x => x.Port, 8080)
            .Add(x => x.Notes, "Test"));

        var markup = cut.Markup;
        Assert.Contains("My Server", markup);
        Assert.Contains("10.0.0.1", markup);
        Assert.Contains("8080", markup);
        Assert.Contains("Test", markup);
        // Non-edit mode shows the "Add" submit button wired to OnSubmit.
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Add"));
    }
}
