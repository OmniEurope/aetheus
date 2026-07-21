// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text.Json;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class PipelineYamlEditorCoordinatorTests
{
    [Fact]
    public async Task LoadSuggestionsAsync_UsesAggregateKeyPagesWithoutLibraryDetails()
    {
        var handler = new SuggestionHandler();
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        var sut = new PipelineYamlEditorCoordinator(api, localizer);

        await sut.LoadSuggestionsAsync(7, null);

        Assert.Contains(handler.Requests, uri =>
            uri.Contains("suggestion-keys", StringComparison.Ordinal)
            && uri.Contains("page=2", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, uri =>
            uri.Contains("api/variable-libraries/", StringComparison.Ordinal)
            && !uri.Contains("names", StringComparison.Ordinal)
            && !uri.Contains("suggestion-keys", StringComparison.Ordinal));
    }

    private sealed class SuggestionHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            Requests.Add(uri);
            object payload = uri.Contains("suggestion-keys", StringComparison.Ordinal)
                ? uri.Contains("page=2", StringComparison.Ordinal)
                    ? new PaginatedResult<string>
                    {
                        Items = ["KEY-201"],
                        TotalCount = 201,
                        Page = 2,
                        PageSize = 200
                    }
                    : new PaginatedResult<string>
                    {
                        Items = Enumerable.Range(1, 200).Select(index => $"KEY-{index}").ToList(),
                        TotalCount = 201,
                        Page = 1,
                        PageSize = 200
                    }
                : new List<string>();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
