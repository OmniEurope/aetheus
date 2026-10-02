// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.CompilerServices;
using Aetheus.Shared.Components.Organizations;

namespace Aetheus.Back.Tests.Architecture;

public sealed class OrganizationRequestConventionTests
{
    [Fact]
    public void OrganizationRequests_AreSealedRecordsWithInitOnlyProperties()
    {
        Type[] requestTypes =
        [
            typeof(CreateOrganizationRequest),
            typeof(UpdateOrganizationRequest),
            typeof(AddOrganizationMemberRequest),
            typeof(UpdateOrganizationMemberRequest),
            typeof(AssignProjectsRequest)
        ];

        foreach (var requestType in requestTypes)
        {
            Assert.True(requestType.IsSealed, $"{requestType.Name} must be sealed.");
            Assert.NotNull(requestType.GetMethod("<Clone>$"));
            Assert.All(requestType.GetProperties(), property =>
                Assert.Contains(
                    typeof(IsExternalInit),
                    property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
        }
    }
}
