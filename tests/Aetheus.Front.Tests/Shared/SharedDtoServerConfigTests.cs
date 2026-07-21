// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Validation;

namespace Aetheus.Front.Tests;

public class SharedDtoServerConfigTests
{
    // --- ServerConfig ---

    [Fact]
    public void ServerConfigYaml_DefaultValues()
    {
        var dto = new ServerConfigYaml();
        Assert.NotNull(dto.Server);
        Assert.Null(dto.Docker);
        Assert.Null(dto.Services);
    }

    [Fact]
    public void ServerConfigServerSection_DefaultValues()
    {
        var dto = new ServerConfigServerSection();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal("Normal", dto.Type);
        Assert.Empty(dto.Tags);
    }

    [Fact]
    public void ServerConfigDockerSection_DefaultValues()
    {
        var dto = new ServerConfigDockerSection();
        Assert.Empty(dto.Containers);
        Assert.Empty(dto.ComposeStacks);
        Assert.Empty(dto.Images);
    }

    [Fact]
    public void ServerConfigContainer_DefaultValues()
    {
        var dto = new ServerConfigContainer();
        Assert.Equal(string.Empty, dto.Image);
        Assert.Equal(string.Empty, dto.Name);
        Assert.Empty(dto.Ports);
        Assert.Empty(dto.Volumes);
        Assert.Equal(string.Empty, dto.Restart);
        Assert.Empty(dto.Env);
    }

    [Fact]
    public void ServerConfigComposeStack_DefaultValues()
    {
        var dto = new ServerConfigComposeStack();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Content);
    }

    [Fact]
    public void ServerConfigServicesSection_DefaultValues()
    {
        var dto = new ServerConfigServicesSection();
        Assert.Empty(dto.Systemd);
    }

    [Fact]
    public void ServerConfigService_DefaultValues()
    {
        var dto = new ServerConfigService();
        Assert.Equal(string.Empty, dto.Name);
        Assert.False(dto.Enabled);
    }

    [Fact]
    public void ServerConfigValidationResult_DefaultValues()
    {
        var dto = new ServerConfigValidationResult();
        Assert.False(dto.IsValid);
        Assert.Empty(dto.Errors);
    }

    [Fact]
    public void ServerConfigPreviewDto_DefaultValues()
    {
        var dto = new ServerConfigPreviewDto();
        Assert.Equal(string.Empty, dto.ServerName);
        Assert.Empty(dto.Changes);
        Assert.Equal(0, dto.TaskCount);
    }

    [Fact]
    public void ServerConfigChange_DefaultValues()
    {
        var dto = new ServerConfigChange();
        Assert.Equal(string.Empty, dto.Category);
        Assert.Equal(string.Empty, dto.Action);
        Assert.Equal(string.Empty, dto.Description);
    }

    [Fact]
    public void ServerConfigDeployResultDto_DefaultValues()
    {
        var dto = new ServerConfigDeployResultDto();
        Assert.Equal(0, dto.TasksCreated);
        Assert.Empty(dto.TaskNames);
    }

    [Fact]
    public void ServerConfigImportRequest_Valid()
    {
        var req = new ServerConfigImportRequest { Yaml = "server:\n  name: web" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void ServerConfigImportRequest_EmptyYaml_Fails()
    {
        var req = new ServerConfigImportRequest { Yaml = "" };
        Assert.NotEmpty(ValidateModel(req));
    }

    // --- MaxItemStringLengthAttribute ---

    private class TestMaxItemModel
    {
        [MaxItemStringLength(5)]
        public List<string>? Tags { get; set; }
    }

    [Fact]
    public void MaxItemStringLength_NullList_Passes()
    {
        var model = new TestMaxItemModel { Tags = null };
        Assert.Empty(ValidateModel(model));
    }

    [Fact]
    public void MaxItemStringLength_EmptyList_Passes()
    {
        var model = new TestMaxItemModel { Tags = [] };
        Assert.Empty(ValidateModel(model));
    }

    [Fact]
    public void MaxItemStringLength_ValidItems_Passes()
    {
        var model = new TestMaxItemModel { Tags = ["abc", "de"] };
        Assert.Empty(ValidateModel(model));
    }

    [Fact]
    public void MaxItemStringLength_TooLongItem_Fails()
    {
        var model = new TestMaxItemModel { Tags = ["short", "toolong"] };
        var results = ValidateModel(model);
        Assert.NotEmpty(results);
        Assert.Contains("index 1", results[0].ErrorMessage!);
    }

    [Fact]
    public void MaxItemStringLength_FirstItemTooLong_Fails()
    {
        var model = new TestMaxItemModel { Tags = ["123456"] };
        var results = ValidateModel(model);
        Assert.NotEmpty(results);
        Assert.Contains("index 0", results[0].ErrorMessage!);
    }

    // --- PaginatedResult ---

    [Fact]
    public void PaginatedResult_DefaultValues()
    {
        var result = new PaginatedResult<string>();
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }
}
