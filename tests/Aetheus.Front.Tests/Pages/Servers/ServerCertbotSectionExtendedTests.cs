// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerCertbotSectionExtendedTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerCertbotSectionExtendedTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/servers/10/certbot/action", true);
        _handler.SetJsonResponse("api/servers/10/certbot/create", true);
    }

    private static ServerDetailDto MakeServer(List<CertbotCertificateDto>? certs = null) => new()
    {
        Id = 10,
        Name = "cert-srv",
        Hostname = "10.0.0.10",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        Tags = [],
        Services = [],
        Certbot = new CertbotDataDto
        {
            IsInstalled = true,
            Version = "2.11.0",
            Certificates = certs ?? [
                new CertbotCertificateDto
                {
                    Name = "example.com",
                    Domains = ["example.com", "www.example.com"],
                    ExpiryDate = DateTime.UtcNow.AddDays(60)  // far away - not expiring soon
                },
                new CertbotCertificateDto
                {
                    Name = "api.example.com",
                    Domains = ["api.example.com"],
                    ExpiryDate = DateTime.UtcNow.AddDays(5)   // within 30 days - expiring soon
                }
            ]
        }
    };

    private IRenderedComponent<ServerCertbotSection> RenderSection(ServerDetailDto? server = null)
    {
        var s = server ?? MakeServer();
        return Render<ServerCertbotSection>(p => p
            .Add(x => x.Server, s)
            .Add(x => x.ServerId, s.Id));
    }

    [Fact]
    public void Renders_WithCertificates_ShowsNames()
    {
        var cut = RenderSection();
        Assert.Contains("example.com", cut.Markup);
    }

    [Fact]
    public void Renders_InstalledBadge_WhenInstalled()
    {
        var cut = RenderSection();
        Assert.Contains("Installed", cut.Markup);
    }

    [Fact]
    public void Renders_Version_InMarkup()
    {
        var cut = RenderSection();
        Assert.Contains("2.11.0", cut.Markup);
    }

    [Fact]
    public void Renders_NotInstalled_WhenNotInstalled()
    {
        var server = MakeServer([]) with
        {
            Certbot = new CertbotDataDto { IsInstalled = false, Certificates = [] }
        };
        var cut = RenderSection(server);
        Assert.Contains("NotInstalled", cut.Markup);
    }

    [Fact]
    public void FilteredCertificates_EmptySearch_ReturnsAll()
    {
        var cut = RenderSection();
        var certs = (List<CertbotCertificateDto>)typeof(ServerCertbotSection)
            .GetProperty("FilteredCertificates", Priv)!
            .GetValue(cut.Instance)!;
        Assert.Equal(2, certs.Count);
    }

    [Fact]
    public void FilteredCertificates_ByName_Filters()
    {
        var cut = RenderSection();
        typeof(ServerCertbotSection).GetField("_certSearch", Priv)!.SetValue(cut.Instance, "api");
        var certs = (List<CertbotCertificateDto>)typeof(ServerCertbotSection)
            .GetProperty("FilteredCertificates", Priv)!
            .GetValue(cut.Instance)!;
        Assert.Single(certs);
        Assert.Equal("api.example.com", certs[0].Name);
    }

    [Fact]
    public void FilteredCertificates_ByDomain_MatchesDomainContains()
    {
        var cut = RenderSection();
        typeof(ServerCertbotSection).GetField("_certSearch", Priv)!.SetValue(cut.Instance, "www.example");
        var certs = (List<CertbotCertificateDto>)typeof(ServerCertbotSection)
            .GetProperty("FilteredCertificates", Priv)!
            .GetValue(cut.Instance)!;
        Assert.Single(certs);
        Assert.Equal("example.com", certs[0].Name);
    }

    [Fact]
    public void CertbotResourceNames_ReturnsAllCertNames()
    {
        var cut = RenderSection();
        var names = (List<string>)typeof(ServerCertbotSection)
            .GetProperty("CertbotResourceNames", Priv)!
            .GetValue(cut.Instance)!;
        Assert.Equal(2, names.Count);
        Assert.Contains("example.com", names);
        Assert.Contains("api.example.com", names);
    }

    [Fact]
    public void ShowConfirm_SetsAllConfirmFields()
    {
        var cut = RenderSection();
        Func<Task> action = () => Task.CompletedTask;
        typeof(ServerCertbotSection).GetMethod("ShowConfirm", Priv)!
            .Invoke(cut.Instance, ["Title", "Message", action]);

        Assert.True((bool)typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.GetValue(cut.Instance)!);
        Assert.Equal("Title", (string)typeof(ServerCertbotSection).GetField("_confirmTitle", Priv)!.GetValue(cut.Instance)!);
        Assert.Equal("Message", (string)typeof(ServerCertbotSection).GetField("_confirmMessage", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task ConfirmAccepted_ExecutesActionAndHides()
    {
        var cut = RenderSection();
        var ran = false;
        typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.SetValue(cut.Instance, true);
        typeof(ServerCertbotSection).GetField("_confirmAction", Priv)!.SetValue(cut.Instance, (Func<Task>)(() => { ran = true; return Task.CompletedTask; }));

        await (Task)typeof(ServerCertbotSection).GetMethod("ConfirmAccepted", Priv)!.Invoke(cut.Instance, [])!;

        Assert.False((bool)typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.GetValue(cut.Instance)!);
        Assert.True(ran);
    }

    [Fact]
    public void ConfirmCancelled_HidesAndClearsAction()
    {
        var cut = RenderSection();
        typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.SetValue(cut.Instance, true);
        typeof(ServerCertbotSection).GetField("_confirmAction", Priv)!.SetValue(cut.Instance, (Func<Task>)(() => Task.CompletedTask));

        typeof(ServerCertbotSection).GetMethod("ConfirmCancelled", Priv)!.Invoke(cut.Instance, []);

        Assert.False((bool)typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.GetValue(cut.Instance)!);
        Assert.Null(typeof(ServerCertbotSection).GetField("_confirmAction", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public async Task ExecuteActionAsync_RenewAll_SetsActionRunningFalseAfter()
    {
        var cut = RenderSection();
        await (Task)typeof(ServerCertbotSection).GetMethod("ExecuteActionAsync", Priv)!
            .Invoke(cut.Instance, [CertbotAction.RenewAll, null])!;
        Assert.False((bool)typeof(ServerCertbotSection).GetField("_actionRunning", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task ExecuteActionAsync_Renew_WithCertName_SendsRequest()
    {
        var cut = RenderSection();
        await (Task)typeof(ServerCertbotSection).GetMethod("ExecuteActionAsync", Priv)!
            .Invoke(cut.Instance, [CertbotAction.Renew, "example.com"])!;
        Assert.False((bool)typeof(ServerCertbotSection).GetField("_actionRunning", Priv)!.GetValue(cut.Instance)!);
    }

    // The create form now lives in ServerCertbotCreateDialog. Hardened rule #4: render it
    // DIRECTLY (in-render-tree, no separate-host hang) and assert the form renders prefilled
    // and the Create button is clickable - do NOT assert on OnClose for a directly-rendered
    // dialog (it does not propagate). The create logic itself is covered via ApiClient tests.
    [Fact]
    public void CreateDialog_RendersForm_CreateButtonWired()
    {
        var cut = Render<ServerCertbotCreateDialog>(p => p.Add(x => x.ServerId, 10));

        var model = typeof(ServerCertbotCreateDialog).GetField("_model", Priv)!.GetValue(cut.Instance)!;
        model.GetType().GetProperty("Domains")!.SetValue(model, "new.example.com");
        model.GetType().GetProperty("Email")!.SetValue(model, "admin@example.com");
        cut.Render(p => p.Add(x => x.ServerId, 10));

        Assert.Contains("Domains", cut.Markup);
        Assert.Contains("Email", cut.Markup);
        cut.FindAll("button").First(b => b.TextContent.Contains("Create")).Click();
        // Clicking Create on the prefilled, valid form POSTs to the certbot create endpoint.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/10/certbot/create"));
    }

    [Fact]
    public void Renders_ExpiringSoon_Badge_WhenCertExpiringSoon()
    {
        var cut = RenderSection();
        Assert.Contains("ExpiringSoon", cut.Markup);
    }

    [Fact]
    public async Task ConfirmAccepted_NullAction_DoesNotThrow()
    {
        var cut = RenderSection();
        typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.SetValue(cut.Instance, true);
        typeof(ServerCertbotSection).GetField("_confirmAction", Priv)!.SetValue(cut.Instance, null);

        await (Task)typeof(ServerCertbotSection).GetMethod("ConfirmAccepted", Priv)!.Invoke(cut.Instance, [])!;
        Assert.False((bool)typeof(ServerCertbotSection).GetField("_confirmVisible", Priv)!.GetValue(cut.Instance)!);
    }
}
