// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerMailSectionRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public ServerMailSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
    }

    private static ServerDetailDto MakeMailServer(bool installed = true) =>
        ServerTestData.MakeMailServer(installed, postfixRunning: installed, dovecotRunning: installed);

    private void StubMailApi(int serverId = 10)
    {
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/domains", new PaginatedResult<MailDomainDto>
        {
            Items = [new() { Id = 1, Name = "example.com", IsActive = true, DkimSelector = "default", HasSpf = true, HasDkim = true }],
            TotalCount = 1
        });
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/accounts", new PaginatedResult<MailAccountDto>
        {
            Items = [new() { Id = 1, Email = "admin@example.com", IsActive = true, QuotaMb = 1024, Domain = "example.com" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/aliases", new PaginatedResult<MailAliasDto>());
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/action", true);
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/setup", true);
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/logs", true);
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/dns", new MailDnsRecordsDto());
        _handler.SetJsonResponse($"api/servers/{serverId}/mail/diagnostics", new MailDiagnosticsDto());
    }

    private IRenderedComponent<ServerMailSection> RenderSection(ServerDetailDto? server = null, int serverId = 10)
    {
        StubMailApi(serverId);
        var s = server ?? MakeMailServer();
        return Render<ServerMailSection>(p => p
            .Add(x => x.Server, s)
            .Add(x => x.ServerId, serverId));
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_059_AnUnconfiguredMailStack_SaysSo_AndGuidesTheSetup()
    {
        var cut = RenderSection(MakeMailServer(installed: false));

        Assert.Contains("MailNotConfigured", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">NotInstalled<", cut.Markup, StringComparison.Ordinal);
        var guide = cut.Find(".mail-setup-guide");
        Assert.Equal(4, guide.QuerySelectorAll(".mail-setup-guide-steps > li").Length);
        Assert.Contains("MailGuidePrerequisites", guide.TextContent, StringComparison.Ordinal);
        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("MailSetupAction", StringComparison.Ordinal));
    }

    [Fact]
    public void Renders_InstalledMail_ShowsPostfixVersion()
    {
        var cut = RenderSection();
        cut.WaitForState(() => cut.Markup.Contains("3.5.6"), TimeSpan.FromSeconds(2));
        Assert.Contains("3.5.6", cut.Markup);
    }

    [Fact]
    public void Renders_NotInstalled_ProducesMarkup()
    {
        var server = MakeMailServer(installed: false);
        var cut = RenderSection(server);

        // The not-installed path renders the setup CTA and never loads the domain grid: no
        // mail/domains GET is issued (OnParametersSetAsync only loads data when Mail.IsInstalled).
        Assert.Contains("MailSetup", cut.Markup);
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("api/servers/10/mail/domains"));
    }

    [Fact]
    public void Renders_WithDomains_ShowsDomainNames()
    {
        var cut = RenderSection();
        cut.WaitForState(() => cut.Markup.Contains("example.com"), TimeSpan.FromSeconds(2));
        Assert.Contains("example.com", cut.Markup);
    }

    [Fact]
    public void Renders_InstalledMail_DoesNotRequestRoutingDiagnosticsOnMount()
    {
        var cut = RenderSection();
        cut.WaitForState(() => cut.Markup.Contains("example.com"), TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("mail/diagnostics", StringComparison.Ordinal));
    }

    [Fact]
    public void SetupWizard_KeepsWhatWasTyped_WhenTheDialogRendersAgain()
    {
        // R-511: the dialog frame re-renders its content on every key press with the same parameters.
        var model = new MailDialogModel();
        var cut = Render<MailOperationDialog>(p => p
            .Add(x => x.Mode, MailDialogMode.Setup)
            .Add(x => x.Model, model));

        cut.Find("#oe-formfield-pages-servers-serverdetailsections-mailoperationdialog-11").Input("mail.example.org");
        cut.Render(p => p.Add(x => x.Mode, MailDialogMode.Setup).Add(x => x.Model, model));

        Assert.Equal("mail.example.org",
            cut.Find("#oe-formfield-pages-servers-serverdetailsections-mailoperationdialog-11").GetAttribute("value"));
        Assert.DoesNotContain("MailSetupDoesNotMoveMail", cut.Markup);
    }

    [Fact]
    public void SetupWizard_ShowsCurrentMxWithoutAnOvhAlert()
    {
        _handler.SetJsonResponse("api/servers/10/mail/mx-preview?domain=example.com", new MailMxPreviewDto
        {
            Domain = "example.com",
            Hosts = ["mx1.ovh.net"],
            Verdict = MailCheckVerdict.Ok
        });
        var cut = Render<MailOperationDialog>(p => p
            .Add(x => x.Mode, MailDialogMode.Setup)
            .Add(x => x.ServerId, 10)
            .Add(x => x.Model, new MailDialogModel
            {
                Hostname = "mx.example.com",
                Domain = "example.com",
                Email = "postmaster@example.com",
                Password = "a-long-enough-password"
            }));

        cut.FindAll("button").First(button => button.TextContent.Contains("MailCheckCurrentRouting", StringComparison.Ordinal)).Click();

        cut.WaitForState(() => cut.Markup.Contains("MailCurrentMxHosts"), TimeSpan.FromSeconds(2));
        Assert.Contains(_handler.Requests, request => request.Url.Contains("mail/mx-preview?domain=example.com", StringComparison.Ordinal));

        cut.FindAll("button").First(button => button.TextContent.Contains("Next", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Next", StringComparison.Ordinal)).Click();
        Assert.DoesNotContain("MailOvhRoutingNotice", cut.Markup);
        Assert.Contains("MailSetupSummary", cut.Markup);
    }

    [Fact]
    public void R2_060_TheSetupSteps_AreNumberedOneToThree_AndEachFieldExplainsItself()
    {
        var cut = Render<MailOperationDialog>(p => p
            .Add(x => x.Mode, MailDialogMode.Setup)
            .Add(x => x.Model, new MailDialogModel()));

        var steps = cut.FindComponents<OmniStepsItem>();
        Assert.Equal(["MailSetupStepServer", "MailSetupStepAdmin", "MailSetupStepOptions"], steps.Select(step => step.Instance.Title).ToArray());
        Assert.DoesNotContain(">4<", cut.Find(".mail-setup-steps").InnerHtml, StringComparison.Ordinal);
        Assert.Equal(["MailHostnameHelp", "MailDomainHelp", "MailDkimSelectorHelp"],
            cut.FindComponents<OmniFormField>().Select(field => field.Instance.Help).Where(help => help is not null).ToArray());
    }

    [Fact]
    public void R2_060_Next_RefusesABareHostName_AndStaysOnTheStep()
    {
        var cut = Render<MailOperationDialog>(p => p
            .Add(x => x.Mode, MailDialogMode.Setup)
            .Add(x => x.Model, new MailDialogModel { Hostname = "vps2577917", Domain = "example.com" }));

        cut.FindAll("button").First(button => button.TextContent.Contains("Next", StringComparison.Ordinal)).Click();

        Assert.Contains("MailHostnameInvalid", cut.Find(".mail-setup-step-errors").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("#oe-formfield-pages-servers-serverdetailsections-mailoperationdialog-11"));

        cut.Find("#oe-formfield-pages-servers-serverdetailsections-mailoperationdialog-11").Input("mx.example.com");
        cut.FindAll("button").First(button => button.TextContent.Contains("Next", StringComparison.Ordinal)).Click();

        Assert.Empty(cut.FindAll(".mail-setup-step-errors"));
        Assert.NotEmpty(cut.FindAll("#oe-formfield-pages-servers-serverdetailsections-mailoperationdialog-14"));
    }

    [Fact]
    public void DnsCheckDialog_ShowsObservedMxWithoutProviderWarning()
    {
        var cut = Render<MailDnsCheckDialog>(p => p.Add(x => x.Check, new MailDnsCheckDto
        {
            Domain = "example.com",
            Records = [new MailDnsRecordCheckDto
            {
                Kind = MailDnsRecordKind.Mx,
                Observed = ["mail.example.com", "mx1.ovh.net"],
                Verdict = MailCheckVerdict.Ok
            }]
        }));

        Assert.Contains("mail.example.com", cut.Markup);
        Assert.Contains("mx1.ovh.net", cut.Markup);
        Assert.DoesNotContain("MailOvhRoutingNotice", cut.Markup);
    }

    // ── LoadDataAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PagedLoaders_PopulateDomainsAndAccounts()
    {
        var cut = RenderSection();
        var domainLoader = typeof(ServerMailSection).GetMethod("LoadDomainsAsync", Priv)!;
        var accountLoader = typeof(ServerMailSection).GetMethod("LoadAccountsAsync", Priv)!;
        var args = new GridLoadArgs { Skip = 0, Top = 25 };

        await cut.InvokeAsync(async () => await (Task)domainLoader.Invoke(cut.Instance, [args])!);
        await cut.InvokeAsync(async () => await (Task)accountLoader.Invoke(cut.Instance, [args])!);

        var domains = (List<MailDomainDto>)typeof(ServerMailSection)
            .GetField("_domains", Priv)!
            .GetValue(cut.Instance)!;
        Assert.NotEmpty(domains);

        var accounts = (List<MailAccountDto>)typeof(ServerMailSection)
            .GetField("_accounts", Priv)!
            .GetValue(cut.Instance)!;
        Assert.NotEmpty(accounts);
    }

    // ── ExecuteActionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteActionAsync_Start_CompletesAndClearsRunning()
    {
        var cut = RenderSection();
        var method = typeof(ServerMailSection).GetMethod("ExecuteActionAsync", Priv)!;

        await cut.InvokeAsync(async () =>
            await (Task)method.Invoke(cut.Instance, [MailAction.StartPostfix])!);

        var running = (bool)typeof(ServerMailSection)
            .GetField("_actionRunning", Priv)!
            .GetValue(cut.Instance)!;
        Assert.False(running);
    }

    [Fact]
    public async Task ExecuteActionAsync_Stop_CompletesAndClearsRunning()
    {
        var cut = RenderSection();
        var method = typeof(ServerMailSection).GetMethod("ExecuteActionAsync", Priv)!;

        await cut.InvokeAsync(async () =>
            await (Task)method.Invoke(cut.Instance, [MailAction.StopPostfix])!);

        Assert.False((bool)typeof(ServerMailSection)
            .GetField("_actionRunning", Priv)!.GetValue(cut.Instance)!);
    }

    // ── Mail dialog model validation ─────────────────────────────────────────

    [Fact]
    public void MailDialogModel_ValidDomain_HasNoValidationError()
    {
        var model = new MailDialogModel { Mode = MailDialogMode.AddDomain, Domain = "example.com" };
        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Empty(errors);
    }

    [Fact]
    public void MailDialogModel_InvalidDomain_HasDomainValidationError()
    {
        var model = new MailDialogModel { Mode = MailDialogMode.AddDomain, Domain = "not_a_domain!" };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(MailDialogModel.Domain)));
    }

    [Fact]
    public void MailDialogModel_EmptyDomain_IsRequired()
    {
        var model = new MailDialogModel { Mode = MailDialogMode.AddDomain, Domain = string.Empty };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(MailDialogModel.Domain)));
    }

    // ── ValidateEmailField ────────────────────────────────────────────────────

    [Fact]
    public void MailDialogModel_ValidAccount_HasNoValidationError()
    {
        var model = new MailDialogModel { Mode = MailDialogMode.AddAccount, Email = "user@example.com", Password = "strong-secret" };
        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Empty(errors);
    }

    [Fact]
    public void MailDialogModel_InvalidEmail_HasEmailValidationError()
    {
        var model = new MailDialogModel { Mode = MailDialogMode.AddAccount, Email = "not-an-email", Password = "strong-secret" };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(MailDialogModel.Email)));
    }

    [Fact]
    public void MailDialogModel_ShortPassword_HasPasswordValidationError()
    {
        var model = new MailDialogModel
        {
            Mode = MailDialogMode.AddAccount,
            Email = "user@example.com",
            Password = new string('x', PasswordPolicy.MinimumLength - 1)
        };

        var errors = model.Validate(new ValidationContext(model)).ToList();

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(MailDialogModel.Password)));
    }

    // ── ShowConfirm / ConfirmCancelled ────────────────────────────────────────

    [Fact]
    public async Task ShowConfirm_UsesNativeDialog()
    {
        var cut = RenderSection();
        var method = typeof(ServerMailSection).GetMethod("ShowConfirm", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["Delete Domain", "Are you sure?", (Func<Task>)(() => Task.CompletedTask)])!);
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();

        Assert.Equal("Delete Domain", dialog.LastTitle);
        Assert.Equal("Are you sure?", dialog.LastConfirmMessage);
    }

    [Fact]
    public async Task ShowConfirm_Cancelled_DoesNotRunAction()
    {
        var cut = RenderSection();
        var executed = false;
        var method = typeof(ServerMailSection).GetMethod("ShowConfirm", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["Delete", "Sure?", (Func<Task>)(() => { executed = true; return Task.CompletedTask; })])!);

        Assert.False(executed);
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(500, "B", "500")]
    [InlineData(1024, "KB", "1")]
    [InlineData(1024 * 1024, "MB", "1")]
    public void FormatBytes_ReturnsHumanReadable(long bytes, string expectedUnit, string expectedNumber)
    {
        var result = ServerMailQueueTab.FormatBytes(bytes);
        Assert.Contains(expectedUnit, result);
        Assert.Contains(expectedNumber, result);
    }

    // ── HandleTaskCompleted ───────────────────────────────────────────────────

    [Fact]
    public void HandleTaskCompleted_DoesNotThrow()
    {
        var cut = RenderSection();
        var ex = Record.Exception(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification { TaskId = 1, ServerId = 10 }));
        Assert.Null(ex);
    }

    // ── ShowDkimRotation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ShowDkimRotation_OpensDialogWithDkimFields()
    {
        var cut = RenderSection();
        var method = typeof(ServerMailSection).GetMethod("ShowDkimRotation", Priv)!;
        var domain = new MailDomainDto { Id = 1, Name = "example.com", DkimSelector = "default" };

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [domain])!);

        var dom = (string)typeof(ServerMailSection).GetField("_dkimRotationDomain", Priv)!.GetValue(cut.Instance)!;
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();

        Assert.Equal("example.com", dom);
        Assert.Equal(typeof(MailOperationDialog), dialog.LastComponent);
        Assert.Equal(MailDialogMode.RotateDkim, dialog.LastParameters!["Mode"]);
        var model = Assert.IsType<MailDialogModel>(dialog.LastParameters["Model"]);
        Assert.Equal("default", model.CurrentSelector);
    }
}
