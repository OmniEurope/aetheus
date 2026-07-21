// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Tests;

public class SharedDtoEnvironmentVaultTests
{
    [Fact]
    public void EnvironmentDto_DefaultValues()
    {
        var dto = new EnvironmentDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Description);
        Assert.Null(dto.ProjectId);
        Assert.Null(dto.ProjectName);
        Assert.False(dto.RequireApproval);
        Assert.Null(dto.ApprovalInstructions);
        Assert.Empty(dto.Servers);
    }

    [Fact]
    public void EnvironmentServerDto_DefaultValues()
    {
        var dto = new EnvironmentServerDto();
        Assert.Equal(0, dto.ServerId);
        Assert.Equal(string.Empty, dto.ServerName);
    }

    [Fact]
    public void CreateEnvironmentRequest_Validation_Valid()
    {
        var req = new CreateEnvironmentRequest { Name = "Staging" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void CreateEnvironmentRequest_Validation_EmptyName_Fails()
    {
        var req = new CreateEnvironmentRequest { Name = "" };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CreateEnvironmentRequest_DefaultType_IsDevelopment()
    {
        var req = new CreateEnvironmentRequest { Name = "t" };
        Assert.Equal(EnvironmentType.Development, req.Type);
    }

    [Fact]
    public void CreateEnvironmentRequest_DefaultTimeout_Is1440()
    {
        var req = new CreateEnvironmentRequest { Name = "t" };
        Assert.Equal(1440, req.ApprovalTimeoutMinutes);
    }

    [Fact]
    public void CreateEnvironmentRequest_Validation_TimeoutOutOfRange_Fails()
    {
        var req = new CreateEnvironmentRequest { Name = "t", ApprovalTimeoutMinutes = 0 };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void UpdateEnvironmentRequest_Validation_Valid()
    {
        var req = new UpdateEnvironmentRequest { Name = "Updated" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void UpdateEnvironmentRequest_DefaultTimeout_Is1440()
    {
        var req = new UpdateEnvironmentRequest { Name = "t" };
        Assert.Equal(1440, req.ApprovalTimeoutMinutes);
    }

    [Fact]
    public void VariableLibraryDto_DefaultValues()
    {
        var dto = new VariableLibraryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Description);
        Assert.Null(dto.ProjectId);
    }

    [Fact]
    public void VariableEntryDto_DefaultValues()
    {
        var dto = new VariableEntryDto();
        Assert.Equal(string.Empty, dto.Key);
        Assert.Equal(string.Empty, dto.Value);
    }

    [Fact]
    public void CreateVariableLibraryRequest_Validation_Valid()
    {
        var req = new CreateVariableLibraryRequest { Name = "Lib" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void CreateVariableLibraryRequest_Validation_EmptyName_Fails()
    {
        var req = new CreateVariableLibraryRequest { Name = "" };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CreateVariableEntryRequest_Validation_Valid()
    {
        var req = new CreateVariableEntryRequest { Key = "MY_KEY", Value = "val" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void UpdateVariableEntryRequest_Validation_Valid()
    {
        var req = new UpdateVariableEntryRequest { Key = "KEY", Value = "newval" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void UpdateVariableLibraryRequest_Validation_Valid()
    {
        var req = new UpdateVariableLibraryRequest { Name = "Updated" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }
}
