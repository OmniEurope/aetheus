// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using System.Text.Json;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using NSubstitute;
using Radzen;

namespace Aetheus.Front.Tests;

internal static class BunitTestHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static TestHandler RegisterServices(BunitContext ctx, bool authenticated = true, bool isAdmin = false)
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var handler = new TestHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var apiClient = new ApiClient(http);

        ctx.Services.AddSingleton(apiClient);
        ctx.Services.AddSingleton<NotificationService>();
        ctx.Services.AddSingleton<DialogService>();
        ctx.Services.AddSingleton<TooltipService>();
        ctx.Services.AddSingleton<ContextMenuService>();

        var authProvider = new AuthStateProvider(Substitute.For<IJSRuntime>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthStateProvider>.Instance);
        if (authenticated)
        {
            var tokenProp = typeof(AuthStateProvider).GetProperty("Token")!;
            tokenProp.SetValue(authProvider, "test-token");

            if (isAdmin)
            {
                var rolesProp = typeof(AuthStateProvider).GetProperty("Roles")!;
                rolesProp.SetValue(authProvider, new List<string> { "Admin" });
            }
        }
        ctx.Services.AddSingleton(authProvider);

        var locMock = Substitute.For<IStringLocalizer<Aetheus.Front.Resources.AppStrings>>();
        locMock[Arg.Any<string>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        locMock[Arg.Any<string>(), Arg.Any<object[]>()]
            .Returns(ci => new LocalizedString((string)ci[0], string.Format((string)ci[0], (object[])ci[1])));
        ctx.Services.AddSingleton(locMock);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiBaseUrl"] = "http://127.0.0.1:1"
            })
            .Build();
        ctx.Services.AddSingleton<IConfiguration>(config);

        ctx.Services.AddSingleton<HubConnectionFactory>(sp =>
            new NullHubConnectionFactory(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<AuthStateProvider>()));
        ctx.Services.AddScoped<BreadcrumbService>();
        ctx.Services.AddScoped<ProjectNavContextService>();
        ctx.Services.AddScoped(sp => new ProjectDetailLoader(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectDetailLoader>.Instance,
            sp.GetRequiredService<HubConnectionFactory>()));
        ctx.Services.AddSingleton(new ActiveOrganizationService(apiClient, Substitute.For<IJSRuntime>()));
        ctx.Services.AddScoped<NotifyHelper>();
        ctx.Services.AddScoped<ClipboardService>();
        ctx.Services.AddScoped<UiActions>();
        ctx.Services.AddScoped<PipelineRunGate>();
        ctx.Services.AddScoped<PipelineRunDialogCoordinator>();
        ctx.Services.AddScoped<PipelineImportState>();
        ctx.Services.AddSingleton(new HelpService(http));

        var httpFactoryMock = Substitute.For<IHttpClientFactory>();
        httpFactoryMock.CreateClient(Arg.Any<string>()).Returns(new HttpClient { BaseAddress = new Uri("http://test/") });
        ctx.Services.AddSingleton(httpFactoryMock);

        var permissionService = new PermissionService();
        if (authenticated)
        {
            var readWritePermissions = Enum.GetValues<ResourceType>()
                .SelectMany(rt => new[]
                {
                    new EffectivePermissionDto { ResourceType = rt, ResourceId = null, Permission = Permission.Read },
                    new EffectivePermissionDto { ResourceType = rt, ResourceId = null, Permission = Permission.Write }
                })
                .ToList();
            permissionService.SetPermissions(readWritePermissions, isAdmin);
        }
        ctx.Services.AddSingleton(permissionService);
        ctx.Services.AddSingleton(TimeProvider.System);
        ctx.Services.AddSingleton<ListCacheService>();
        ctx.Services.AddSingleton(sp => new TaskTrackerService(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<HubConnectionFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskTrackerService>.Instance));
        ctx.Services.AddSingleton(sp => new AlertNotificationService(
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<HubConnectionFactory>()));
        ctx.Services.AddScoped<ConfirmHelper>();
        ctx.Services.AddScoped(sp => new UserNotificationService(
            sp.GetRequiredService<HubConnectionFactory>(),
            sp.GetRequiredService<AuthStateProvider>(),
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<PermissionService>(),
            TimeProvider.System));

        return handler;
    }

    public static void UseImmediateDialogs(BunitContext ctx) =>
        ctx.Services.AddSingleton<DialogService>(services => new ImmediateDialogService(
            services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            services.GetRequiredService<IJSRuntime>()));

    public static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj, JsonOpts), System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Lightweight <see cref="IStringLocalizer{T}"/> that returns the key as the value,
    /// usable outside DI (e.g. with <c>RuntimeHelpers.GetUninitializedObject</c>).
    /// </summary>
    internal sealed class StubLocalizer : IStringLocalizer<Aetheus.Front.Resources.AppStrings>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    internal sealed class NullHubConnectionFactory(IConfiguration config, AuthStateProvider auth)
        : HubConnectionFactory(config, auth, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthDelegatingHandler>.Instance)
    {
        public override HubConnection Create(string hubPath, IRetryPolicy? retryPolicy = null)
        {
            return new HubConnectionBuilder()
                .WithUrl($"http://127.0.0.1:1/hubs/{hubPath}", opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => new ImmediateFailHandler();
                })
                .Build();
        }

        private sealed class ImmediateFailHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromException<HttpResponseMessage>(new HttpRequestException("Test: hub not available"));
        }
    }

    internal sealed class TestHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new(StringComparer.OrdinalIgnoreCase);
        // Method-aware responses take precedence over URL-only ones, so a POST and a GET to the same
        // URL can return different payloads (e.g. a create POST returning the created DTO vs the
        // list-reload GET returning a collection). Longest URL-substring wins, as for _responses.
        private readonly List<(string Method, string UrlContains, Func<HttpResponseMessage> Make)> _methodResponses = [];
        private readonly List<(string Method, string UrlContains, Func<CancellationToken, Task<HttpResponseMessage>> Make)> _asyncMethodResponses = [];

        /// <summary>Every request seen, in order - lets a test assert a POST/PUT/DELETE was actually sent.</summary>
        public List<(string Method, string Url)> Requests { get; } = [];
        public List<(string Method, string Url, string? Body)> RequestDetails { get; } = [];
        public string? LastRequestBody { get; private set; }

        public void SetResponse(string urlContains, HttpStatusCode status) =>
            _responses[urlContains] = () => new HttpResponseMessage(status) { Content = new StringContent("{}") };

        public void SetJsonResponse<T>(string urlContains, T data)
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            _responses[urlContains] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }

        public void SetPaginatedJsonResponse<T>(string urlContains, IEnumerable<T> data)
        {
            var items = data.ToList();
            SetJsonResponse(urlContains, new PaginatedResult<T>
            {
                Items = items,
                TotalCount = items.Count,
                Page = 1,
                PageSize = Math.Max(items.Count, 1)
            });
        }

        /// <summary>
        /// Returns a raw <c>200</c> body verbatim (no serialization) so a test can feed a MALFORMED
        /// JSON payload and prove the caller survives a <see cref="JsonException"/> instead of crashing.
        /// </summary>
        public void SetRawResponse(string urlContains, string body) =>
            _responses[urlContains] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };

        public void SetRawResponse(HttpMethod method, string urlContains, string body, string contentType)
        {
            _methodResponses.RemoveAll(response =>
                string.Equals(response.Method, method.Method, StringComparison.OrdinalIgnoreCase)
                && string.Equals(response.UrlContains, urlContains, StringComparison.OrdinalIgnoreCase));
            _methodResponses.Add((method.Method, urlContains, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
            }));
        }

        public void SetJsonResponse<T>(HttpMethod method, string urlContains, T data)
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            _methodResponses.RemoveAll(response =>
                string.Equals(response.Method, method.Method, StringComparison.OrdinalIgnoreCase)
                && string.Equals(response.UrlContains, urlContains, StringComparison.OrdinalIgnoreCase));
            _methodResponses.Add((method.Method, urlContains, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }));
        }

        public void SetPaginatedJsonResponse<T>(HttpMethod method, string urlContains, IEnumerable<T> data)
        {
            var items = data.ToList();
            SetJsonResponse(method, urlContains, new PaginatedResult<T>
            {
                Items = items,
                TotalCount = items.Count,
                Page = 1,
                PageSize = Math.Max(items.Count, 1)
            });
        }

        public void SetResponse(HttpMethod method, string urlContains, HttpStatusCode status)
        {
            _methodResponses.RemoveAll(response =>
                string.Equals(response.Method, method.Method, StringComparison.OrdinalIgnoreCase)
                && string.Equals(response.UrlContains, urlContains, StringComparison.OrdinalIgnoreCase));
            _methodResponses.Add((method.Method, urlContains,
                () => new HttpResponseMessage(status) { Content = new StringContent("{}") }));
        }

        public void SetAsyncJsonResponse<T>(
            HttpMethod method,
            string urlContains,
            Func<CancellationToken, Task<T>> responseFactory)
        {
            _asyncMethodResponses.RemoveAll(response =>
                string.Equals(response.Method, method.Method, StringComparison.OrdinalIgnoreCase)
                && string.Equals(response.UrlContains, urlContains, StringComparison.OrdinalIgnoreCase));
            _asyncMethodResponses.Add((method.Method, urlContains, async ct =>
            {
                var data = await responseFactory(ct);
                var json = JsonSerializer.Serialize(data, JsonOpts);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }
            ));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add((request.Method.Method, url));
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            RequestDetails.Add((request.Method.Method, url, LastRequestBody));
            foreach (var r in _asyncMethodResponses
                         .Where(r => string.Equals(r.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase)
                                     && url.Contains(r.UrlContains, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(r => r.UrlContains.Length))
                return await r.Make(ct);
            // Method-aware responses win first (so POST vs GET on the same URL can differ).
            foreach (var r in _methodResponses
                         .Where(r => string.Equals(r.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase)
                                     && url.Contains(r.UrlContains, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(r => r.UrlContains.Length))
                return r.Make();
            // Match longest pattern first to avoid partial matches (e.g. "api/pipelines/5" vs "api/pipelines/5/runs")
            foreach (var kvp in _responses.OrderByDescending(k => k.Key.Length))
            {
                if (url.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value();
            }
            throw new InvalidOperationException(
                $"TestHandler: no mocked response for '{request.Method} {url}'. "
                + "Add a method-aware SetJsonResponse/SetResponse for the exact operation.");
        }
    }
}
