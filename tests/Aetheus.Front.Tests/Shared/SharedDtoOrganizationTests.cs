// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Tests;

public class SharedDtoOrganizationTests
{
    [Fact]
    public void CreateOrganizationRequest_Validation_Valid()
    {
        var req = new CreateOrganizationRequest { Name = "My Org", Slug = "my-org", Description = "desc" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void CreateOrganizationRequest_Validation_EmptyName_Fails()
    {
        var req = new CreateOrganizationRequest { Name = "", Slug = "valid-slug" };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CreateOrganizationRequest_Validation_InvalidSlug_Fails()
    {
        var req = new CreateOrganizationRequest { Name = "Valid", Slug = "INVALID SLUG!" };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CreateOrganizationRequest_Validation_SlugWithHyphens_Passes()
    {
        var req = new CreateOrganizationRequest { Name = "Valid", Slug = "my-org-123" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void UpdateOrganizationRequest_Validation_Valid()
    {
        var req = new UpdateOrganizationRequest { Name = "Updated", Slug = "updated" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void UpdateOrganizationRequest_Validation_TooLongName_Fails()
    {
        var req = new UpdateOrganizationRequest { Name = new string('x', 101), Slug = "ok" };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void AddOrganizationMemberRequest_DefaultRole_IsMember()
    {
        var req = new AddOrganizationMemberRequest { UserId = 1 };
        Assert.Equal(OrganizationRole.Member, req.Role);
    }

    [Fact]
    public void AssignProjectsRequest_DefaultEmpty()
    {
        var req = new AssignProjectsRequest();
        Assert.Empty(req.ProjectIds);
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }
}
