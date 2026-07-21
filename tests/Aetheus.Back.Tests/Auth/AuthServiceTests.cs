// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.Organizations;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class AuthServiceTests
{
    private readonly IAuthRepository _repo = Substitute.For<IAuthRepository>();
    private readonly IConfiguration _config;
    private readonly AuthService _sut;
    private readonly ServerEnrollmentService _enrollment;

    public AuthServiceTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AdminUser"] = "admin",
                ["Auth:AdminPassword"] = "secret",
                ["Auth:JwtKey"] = "aetheus-dev-key-minimum-32-bytes!!",
                ["Auth:ExternalLogin:Enabled"] = "true",
                ["Auth:ExternalLogin:AllowedProviders:0"] = "github",
                ["Auth:ExternalLogin:AllowedProviders:1"] = "google",
                ["Auth:ExternalLogin:AutoProvision"] = "true"
            })
            .Build();

        var hubClients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubClients.Group(Arg.Any<string>()).Returns(clientProxy);
        hubClients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<ServerHub>>();
        hubContext.Clients.Returns(hubClients);

        _repo.When(r => r.AddServer(Arg.Any<Server>())).Do(ci => ci.Arg<Server>().Id = 1);

        var orgRepo = Substitute.For<IOrganizationService>();
        orgRepo.GetDefaultOrganizationIdAsync(Arg.Any<CancellationToken>())
            .Returns(1);

        var audit = Substitute.For<IAuditService>();
        var jwtOptions = new JwtOptions { SigningKey = "aetheus-dev-key-minimum-32-bytes!!" };

        _sut = new AuthService(
            _repo,
            _config,
            audit,
            jwtOptions,
                Substitute.For<IMemoryCache>(),
                orgRepo,
                Substitute.For<ITotpService>(),
                TimeProvider.System,
                Substitute.For<IHttpContextAccessor>(),
                Substitute.For<Aetheus.Back.Services.IAdminChangeNotifier>(),
                Substitute.For<ILogger<AuthService>>());

        _enrollment = new ServerEnrollmentService(_repo, _config, audit, hubContext, Substitute.For<Aetheus.Back.Services.IAdminChangeNotifier>(), jwtOptions, TimeProvider.System);
    }

    // --- LoginAsync ---

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        var result = await _sut.LoginAsync(new LoginRequest { Username = "admin", Password = "secret" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_InvalidUser_ReturnsNull()
    {
        var result = await _sut.LoginAsync(new LoginRequest { Username = "wrong", Password = "secret" }, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsNull()
    {
        var result = await _sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    // --- CreateRegistrationTokenAsync ---

    [Fact]
    public async Task CreateRegistrationTokenAsync_ReturnsDto()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.CreateRegistrationTokenAsync(new CreateRegistrationTokenRequest { ExpirationHours = 48 }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.False(result.IsUsed);
        _repo.Received(1).AddRegistrationToken(Arg.Any<RegistrationToken>());
    }

    [Fact]
    public async Task CreateRegistrationTokenAsync_DefaultExpiration_24Hours()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.CreateRegistrationTokenAsync(new CreateRegistrationTokenRequest { ExpirationHours = 0 }, ct: TestContext.Current.CancellationToken);

        Assert.True(result.ExpiresAt > DateTime.UtcNow.AddHours(23));
        Assert.True(result.ExpiresAt < DateTime.UtcNow.AddHours(25));
    }

    // --- GetRegistrationTokensAsync ---

    [Fact]
    public async Task GetRegistrationTokensAsync_MapsEntities()
    {
        _repo.GetRegistrationTokensAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new RegistrationToken { Id = 1, Token = "tok1", IsUsed = false, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) },
            new RegistrationToken { Id = 2, Token = "tok2", IsUsed = true, UsedByServerId = 5, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) }
        ]);

        var result = await _sut.GetRegistrationTokensAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsUsed);
        Assert.True(result[1].IsUsed);
        Assert.Equal(5, result[1].UsedByServerId);
    }

    // --- ValidateServerTokenAsync ---

    [Fact]
    public async Task ValidateServerTokenAsync_ValidToken_ReturnsServerId()
    {
        _repo.ValidateServerTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var result = await _sut.ValidateServerTokenAsync("some-token", ct: TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ValidateServerTokenAsync_InvalidToken_ReturnsNull()
    {
        _repo.ValidateServerTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);

        var result = await _sut.ValidateServerTokenAsync("bad-token", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // --- RegisterServerAsync ---

    [Fact]
    public async Task RegisterServerAsync_InvalidToken_ReturnsNull()
    {
        _repo.FindValidRegistrationTokenAsync("bad", Arg.Any<CancellationToken>())
            .Returns((RegistrationToken?)null);

        var result = await _enrollment.RegisterServerAsync(new ServerRegistrationRequest
        {
            RegistrationToken = "bad",
            Hostname = "host",
            OsDescription = "Linux",
            AgentVersion = "1.0",
            IpAddress = "10.0.0.1"
        }, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RegisterServerAsync_NewServer_CreatesAndReturns()
    {
        var regToken = new RegistrationToken { Id = 1, Token = "valid-token" };
        _repo.FindValidRegistrationTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(regToken);
        _repo.FindServerByHostnameAsync("new-host", Arg.Any<CancellationToken>())
            .Returns((Server?)null);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.ConsumeRegistrationTokenAsync(regToken.Id, Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _enrollment.RegisterServerAsync(new ServerRegistrationRequest
        {
            RegistrationToken = "valid-token",
            Hostname = "new-host",
            OsDescription = "Linux",
            AgentVersion = "1.0",
            IpAddress = "10.0.0.1"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.BearerToken));
        _repo.Received(1).AddServer(Arg.Any<Server>());
        _repo.Received(1).AddServerToken(Arg.Any<ServerToken>());
    }

    [Fact]
    public async Task RegisterServerAsync_ExistingServer_UpdatesAndReturns()
    {
        var regToken = new RegistrationToken { Id = 1, Token = "valid-token" };
        var existingServer = new Server { Id = 5, Name = "existing", Hostname = "existing-host" };
        _repo.FindValidRegistrationTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(regToken);
        _repo.FindServerByHostnameAsync("existing-host", Arg.Any<CancellationToken>())
            .Returns(existingServer);
        _repo.ServerHasActiveTokensAsync(existingServer.Id, Arg.Any<CancellationToken>())
            .Returns(false);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.ConsumeRegistrationTokenAsync(regToken.Id, Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _enrollment.RegisterServerAsync(new ServerRegistrationRequest
        {
            RegistrationToken = "valid-token",
            Hostname = "existing-host",
            OsDescription = "Ubuntu 24.04",
            AgentVersion = "2.0",
            IpAddress = "10.0.0.2"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(5, result.ServerId);
        Assert.Equal("Ubuntu 24.04", existingServer.OsDescription);
        _repo.DidNotReceive().AddServer(Arg.Any<Server>());
    }

    // --- Enrollment: PipelineRunnerEnabled default (secure-by-default gate) ---

    [Fact]
    public async Task RegisterServerAsync_PipelineRunnerAvailableFalse_DisablesRunner()
    {
        var regToken = new RegistrationToken { Id = 1, Token = "valid-token" };
        Server? captured = null;
        _repo.FindValidRegistrationTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(regToken);
        _repo.FindServerByHostnameAsync("new-host", Arg.Any<CancellationToken>()).Returns((Server?)null);
        _repo.When(r => r.AddServer(Arg.Any<Server>())).Do(ci => { captured = ci.Arg<Server>(); captured.Id = 1; });
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.ConsumeRegistrationTokenAsync(regToken.Id, Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _enrollment.RegisterServerAsync(new ServerRegistrationRequest
        {
            RegistrationToken = "valid-token",
            Hostname = "new-host",
            OsDescription = "Linux",
            AgentVersion = "1.0",
            IpAddress = "10.0.0.1",
            PipelineRunnerAvailable = false
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(captured);
        Assert.False(captured.PipelineRunnerEnabled);
    }

    [Fact]
    public async Task RegisterServerAsync_PipelineRunnerAvailableNull_EnablesRunnerByDefault()
    {
        // A pre-feature agent omits the field (null) → keep the legacy enabled default.
        var regToken = new RegistrationToken { Id = 1, Token = "valid-token" };
        Server? captured = null;
        _repo.FindValidRegistrationTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(regToken);
        _repo.FindServerByHostnameAsync("new-host", Arg.Any<CancellationToken>()).Returns((Server?)null);
        _repo.When(r => r.AddServer(Arg.Any<Server>())).Do(ci => { captured = ci.Arg<Server>(); captured.Id = 1; });
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.ConsumeRegistrationTokenAsync(regToken.Id, Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _enrollment.RegisterServerAsync(new ServerRegistrationRequest
        {
            RegistrationToken = "valid-token",
            Hostname = "new-host",
            OsDescription = "Linux",
            AgentVersion = "1.0",
            IpAddress = "10.0.0.1",
            PipelineRunnerAvailable = null
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(captured);
        Assert.True(captured.PipelineRunnerEnabled);
    }

    // --- RotateAgentTokenAsync ---

    [Fact]
    public async Task RotateAgentTokenAsync_ServerNotFound_ReturnsNull()
    {
        _repo.ServerExistsAsync(99, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.RotateAgentTokenAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RotateAgentTokenAsync_ServerExists_RevokesAndReturnsNewToken()
    {
        _repo.ServerExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _repo.RevokeServerTokensAsync(1, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.RotateAgentTokenAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(1, result.ServerId);
        Assert.False(string.IsNullOrEmpty(result.BearerToken));
        await _repo.Received(1).RevokeServerTokensAsync(1, Arg.Any<CancellationToken>());
    }

    // --- HandleExternalLoginAsync ---

    [Fact]
    public async Task HandleExternalLoginAsync_ExistingLogin_ReturnsToken()
    {
        var user = new User { Id = 1, Username = "ext-user", IsActive = true, UserRoles = [new UserRole { Role = new Role { Name = "User" } }] };
        _repo.FindExternalLoginAsync("github", "123", Arg.Any<CancellationToken>())
            .Returns(new ExternalLogin { UserId = 1, Provider = "github", ProviderSubjectId = "123", User = user });

        var result = await _sut.HandleExternalLoginAsync("github", "123", "ext-user", "ext@test.com", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }

    [Fact]
    public async Task HandleExternalLoginAsync_InactiveUser_ReturnsNull()
    {
        var user = new User { Id = 2, Username = "inactive", IsActive = false, UserRoles = [] };
        _repo.FindExternalLoginAsync("github", "456", Arg.Any<CancellationToken>())
            .Returns(new ExternalLogin { UserId = 2, Provider = "github", ProviderSubjectId = "456", User = user });

        var result = await _sut.HandleExternalLoginAsync("github", "456", "inactive", null, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleExternalLoginAsync_NewUser_CreatesAndReturnsToken()
    {
        // Canonical username for newly provisioned external identities is "{provider}_{subjectId}".
        // displayName is intentionally NOT used to look up an existing local account (prevents takeover).
        const string canonical = "google_789";

        _repo.FindExternalLoginAsync("google", "789", Arg.Any<CancellationToken>())
            .Returns((ExternalLogin?)null);
        _repo.AddUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.AddExternalLoginAsync(Arg.Any<ExternalLogin>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // First call (collision check) returns null; second call (refetch with roles) returns the new user.
        _repo.FindUserWithRolesAsync(canonical, Arg.Any<CancellationToken>())
            .Returns((User?)null, new User { Id = 10, Username = canonical, IsActive = true, UserRoles = [], SecurityStamp = "stamp" });

        var result = await _sut.HandleExternalLoginAsync("google", "789", "NewUser", "new@test.com", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repo.Received(1).AddUserAsync(Arg.Is<User>(u => u.Username == canonical), Arg.Any<CancellationToken>());
        await _repo.Received(1).AddExternalLoginAsync(Arg.Any<ExternalLogin>(), Arg.Any<CancellationToken>());
    }
}
