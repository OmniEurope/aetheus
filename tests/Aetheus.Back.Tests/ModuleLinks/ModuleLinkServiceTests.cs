// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ModuleLinks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ModuleLinkServiceTests
{
    private readonly IModuleLinkRepository _repoMock = Substitute.For<IModuleLinkRepository>();
    private readonly ModuleLinkService _sut;

    public ModuleLinkServiceTests()
    {
        _sut = new ModuleLinkService(_repoMock);
    }

    [Fact]
    public async Task GetLinksAsync_ReturnsMappedDtos()
    {
        _repoMock.GetLinksAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new ModuleLink { Id = 1, ServerId = 1, SourceType = ModuleLinkType.Apache, SourceIdentifier = "site.com", TargetType = ModuleLinkType.Certbot, TargetIdentifier = "cert1", IsAutoDetected = true },
                new ModuleLink { Id = 2, ServerId = 1, SourceType = ModuleLinkType.Docker, SourceIdentifier = "nginx", TargetType = ModuleLinkType.Apache, TargetIdentifier = "proxy", IsAutoDetected = false }
            ]);

        var result = await _sut.GetLinksAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(ModuleLinkType.Apache, result[0].SourceType);
        Assert.Equal("site.com", result[0].SourceIdentifier);
        Assert.True(result[0].IsAutoDetected);
    }

    [Fact]
    public async Task GetLinksForResourceAsync_ReturnsOtherSideOfLink_WhenSource()
    {
        _repoMock.GetLinksForResourceAsync(1, ModuleLinkType.Apache, "site.com", Arg.Any<CancellationToken>())
            .Returns([
                new ModuleLink { Id = 1, SourceType = ModuleLinkType.Apache, SourceIdentifier = "site.com", TargetType = ModuleLinkType.Certbot, TargetIdentifier = "cert1", IsAutoDetected = true }
            ]);

        var result = await _sut.GetLinksForResourceAsync(1, ModuleLinkType.Apache, "site.com", ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(ModuleLinkType.Certbot, result[0].Type);
        Assert.Equal("cert1", result[0].Identifier);
    }

    [Fact]
    public async Task GetLinksForResourceAsync_ReturnsSourceSide_WhenTarget()
    {
        _repoMock.GetLinksForResourceAsync(1, ModuleLinkType.Certbot, "cert1", Arg.Any<CancellationToken>())
            .Returns([
                new ModuleLink { Id = 1, SourceType = ModuleLinkType.Apache, SourceIdentifier = "site.com", TargetType = ModuleLinkType.Certbot, TargetIdentifier = "cert1", IsAutoDetected = false }
            ]);

        var result = await _sut.GetLinksForResourceAsync(1, ModuleLinkType.Certbot, "cert1", ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(ModuleLinkType.Apache, result[0].Type);
        Assert.Equal("site.com", result[0].Identifier);
    }

    [Fact]
    public async Task GetLinksPageAsync_MapsOtherSideAndPaginationMetadata()
    {
        var request = new ModuleLinkPageRequest
        {
            SourceType = ModuleLinkType.Docker,
            ResourceIdentifiers = ["web"],
            Page = 2,
            PageSize = 25
        };
        _repoMock.GetLinksPageAsync(1, request, Arg.Any<CancellationToken>())
            .Returns((
                [new ModuleLink
                {
                    Id = 4,
                    SourceType = ModuleLinkType.Docker,
                    SourceIdentifier = "web",
                    TargetType = ModuleLinkType.Apache,
                    TargetIdentifier = "site.conf"
                }],
                30));

        var result = await _sut.GetLinksPageAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.Equal(30, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(25, result.PageSize);
        var linked = Assert.Single(result.Items);
        Assert.Equal(ModuleLinkType.Apache, linked.Type);
        Assert.Equal("site.conf", linked.Identifier);
    }

    [Fact]
    public async Task CreateLinkAsync_Success_ReturnsDto()
    {
        _repoMock.LinkExistsAsync(1, ModuleLinkType.Apache, "site.com", ModuleLinkType.Certbot, "cert1", Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateModuleLinkRequest
        {
            SourceType = ModuleLinkType.Apache,
            SourceIdentifier = "site.com",
            TargetType = ModuleLinkType.Certbot,
            TargetIdentifier = "cert1"
        };

        var result = await _sut.CreateLinkAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.Equal("site.com", result.SourceIdentifier);
        Assert.False(result.IsAutoDetected);
        await _repoMock.Received(1).AddAsync(Arg.Any<ModuleLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateLinkAsync_Conflict_ThrowsConflictException()
    {
        _repoMock.LinkExistsAsync(1, ModuleLinkType.Apache, "site.com", ModuleLinkType.Certbot, "cert1", Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new CreateModuleLinkRequest
        {
            SourceType = ModuleLinkType.Apache,
            SourceIdentifier = "site.com",
            TargetType = ModuleLinkType.Certbot,
            TargetIdentifier = "cert1"
        };

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateLinkAsync(1, request, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteLinkAsync_Found_Deletes()
    {
        var link = new ModuleLink { Id = 5, ServerId = 1 };
        _repoMock.FindLinkAsync(5, Arg.Any<CancellationToken>())
            .Returns(link);

        await _sut.DeleteLinkAsync(5, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).RemoveAsync(link, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteLinkAsync_NotFound_ThrowsNotFoundException()
    {
        _repoMock.FindLinkAsync(999, Arg.Any<CancellationToken>())
            .Returns((ModuleLink?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteLinkAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AutoDetectLinksAsync_MatchesDomains_CreatesLinks()
    {
        _repoMock.GetApacheVhostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ApacheVirtualHost { ServerId = 1, ServerName = "example.com", Port = 80, DocumentRoot = "/var/www" }]);
        _repoMock.GetCertbotCertsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new CertbotCertificate { ServerId = 1, Name = "le-cert", Domains = "[\"example.com\",\"www.example.com\"]" }]);

        List<ModuleLink>? savedLinks = null;
        _repoMock.AddLinksAsync(Arg.Any<List<ModuleLink>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { savedLinks = ci.Arg<List<ModuleLink>>(); return Task.CompletedTask; });

        await _sut.AutoDetectLinksAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(savedLinks);
        Assert.Single(savedLinks);
        Assert.True(savedLinks[0].IsAutoDetected);
        Assert.Equal("example.com", savedLinks[0].SourceIdentifier);
        Assert.Equal("le-cert", savedLinks[0].TargetIdentifier);
    }

    [Fact]
    public async Task AutoDetectLinksAsync_NoMatch_CreatesNoLinks()
    {
        _repoMock.GetApacheVhostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ApacheVirtualHost { ServerId = 1, ServerName = "site-a.com", Port = 80, DocumentRoot = "/var/www" }]);
        _repoMock.GetCertbotCertsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new CertbotCertificate { ServerId = 1, Name = "cert-for-b", Domains = "[\"site-b.com\"]" }]);

        await _sut.AutoDetectLinksAsync(1, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().AddLinksAsync(Arg.Any<List<ModuleLink>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoDetectLinksAsync_DeduplicatesLinks()
    {
        _repoMock.GetApacheVhostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new ApacheVirtualHost { ServerId = 1, ServerName = "example.com", Port = 80, DocumentRoot = "/var/www" },
                new ApacheVirtualHost { ServerId = 1, ServerName = "example.com", Port = 443, DocumentRoot = "/var/www" }
            ]);
        _repoMock.GetCertbotCertsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new CertbotCertificate { ServerId = 1, Name = "cert", Domains = "[\"example.com\"]" }]);

        List<ModuleLink>? savedLinks = null;
        _repoMock.AddLinksAsync(Arg.Any<List<ModuleLink>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { savedLinks = ci.Arg<List<ModuleLink>>(); return Task.CompletedTask; });

        await _sut.AutoDetectLinksAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(savedLinks);
        Assert.Single(savedLinks);
    }

    [Fact]
    public async Task AutoDetectLinksAsync_InvalidJson_HandlesGracefully()
    {
        _repoMock.GetApacheVhostsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new ApacheVirtualHost { ServerId = 1, ServerName = "x.com", Port = 80, DocumentRoot = "/var/www" }]);
        _repoMock.GetCertbotCertsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new CertbotCertificate { ServerId = 1, Name = "cert", Domains = "not-json" }]);

        await _sut.AutoDetectLinksAsync(1, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().AddLinksAsync(Arg.Any<List<ModuleLink>>(), Arg.Any<CancellationToken>());
    }
}
