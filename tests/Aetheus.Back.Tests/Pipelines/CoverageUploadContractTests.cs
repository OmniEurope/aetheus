// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Tests.Pipelines;

public sealed class CoverageUploadContractTests
{
    [Fact]
    public void RawCoverageEndpoint_IsXmlAndBoundedByUtf8RequestBytes()
    {
        var method = typeof(PipelinesController).GetMethod(nameof(PipelinesController.PublishCoverageRaw));

        Assert.NotNull(method);
        var consumes = Assert.Single(method!.GetCustomAttributes(typeof(ConsumesAttribute), true)
            .Cast<ConsumesAttribute>());
        Assert.Contains("application/xml", consumes.ContentTypes);
        var limit = Assert.Single(method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), true)
            .Cast<RequestSizeLimitAttribute>());
        Assert.Equal(
            CoverageUploadLimits.MaxRawXmlBytes,
            ((IRequestSizeLimitMetadata)limit).MaxRequestBodySize);
    }
}
