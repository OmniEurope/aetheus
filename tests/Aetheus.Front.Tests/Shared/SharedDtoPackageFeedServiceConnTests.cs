// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Front.Tests;

public class SharedDtoPackageFeedServiceConnTests
{
    // --- PackageFeed ---

    [Fact]
    public void PackageFeedDto_DefaultValues()
    {
        var dto = new PackageFeedDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.UpstreamUrl);
        Assert.Null(dto.Description);
        Assert.Null(dto.ProjectId);
        Assert.Null(dto.ProjectName);
        Assert.Null(dto.ServiceConnectionName);
        Assert.Equal(0, dto.PackageCount);
    }

    [Fact]
    public void PackageFeedDetailDto_DefaultValues()
    {
        var dto = new PackageFeedDetailDto();
        Assert.Empty(dto.Packages);
        Assert.Null(dto.ServiceConnectionId);
    }

    [Fact]
    public void CreatePackageFeedRequest_Valid()
    {
        var req = new CreatePackageFeedRequest { Name = "Feed", UpstreamUrl = "https://example.com" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void CreatePackageFeedRequest_EmptyName_Fails()
    {
        var req = new CreatePackageFeedRequest { Name = "", UpstreamUrl = "https://example.com" };
        Assert.NotEmpty(ValidateModel(req));
    }

    [Fact]
    public void UpdatePackageFeedRequest_Valid()
    {
        var req = new UpdatePackageFeedRequest { Name = "Updated", UpstreamUrl = "https://example.com" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void PackageEntryDto_DefaultValues()
    {
        var dto = new PackageEntryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.LatestVersion);
        Assert.Null(dto.Description);
    }

    // --- ServiceConnection ---

    [Fact]
    public void ServiceConnectionDto_DefaultValues()
    {
        var dto = new ServiceConnectionDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Null(dto.Description);
        Assert.Null(dto.ProjectId);
        Assert.Null(dto.Url);
    }

    [Fact]
    public void ServiceConnectionDetailDto_DefaultValues()
    {
        var dto = new ServiceConnectionDetailDto();
        Assert.Equal("{}", dto.ConfigurationJson);
    }

    [Fact]
    public void CreateServiceConnectionRequest_Valid()
    {
        var req = new CreateServiceConnectionRequest { Name = "Conn", ConfigurationJson = "{}" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void CreateServiceConnectionRequest_EmptyName_Fails()
    {
        var req = new CreateServiceConnectionRequest { Name = "", ConfigurationJson = "{}" };
        Assert.NotEmpty(ValidateModel(req));
    }

    [Fact]
    public void UpdateServiceConnectionRequest_Valid()
    {
        var req = new UpdateServiceConnectionRequest { Name = "Updated", ConfigurationJson = "{}" };
        Assert.Empty(ValidateModel(req));
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }
}
