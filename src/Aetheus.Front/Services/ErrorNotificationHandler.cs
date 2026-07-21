// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Services;

public sealed class ErrorNotificationHandler(NotificationService notificationService, IStringLocalizer<AppStrings> localizer) : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> SilentCodes =
    [
        HttpStatusCode.Unauthorized // Handled by AuthDelegatingHandler
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode && !SilentCodes.Contains(response.StatusCode))
        {
            // On Blazor WASM the response body is a one-shot BrowserHttpReadStream. Reading it here to
            // surface the API error consumes and disposes it, so HttpClient's own post-pipeline buffering
            // (HttpCompletionOption.ResponseContentRead) then throws ObjectDisposedException and the
            // failure bubbles up to the ErrorBoundary instead of the caller's normal non-2xx handling.
            // Buffering first materialises a re-readable copy so both this read and HttpClient's succeed.
            // Mandatory try/catch: a throwing DelegatingHandler would break every error response.
            try
            {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ErrorNotification] response buffering failed: {ex.Message}");
            }

            var message = await ExtractErrorMessageAsync(response, cancellationToken);
            var severity = response.StatusCode >= HttpStatusCode.InternalServerError
                ? NotificationSeverity.Error
                : NotificationSeverity.Warning;

            notificationService.Notify(new NotificationMessage
            {
                Severity = severity,
                Summary = $"{localizer["Error"]} {(int)response.StatusCode}",
                Detail = message,
                Duration = 5000
            });
        }

        return response;
    }

    private async Task<string> ExtractErrorMessageAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions.Web, ct);
                if (!string.IsNullOrEmpty(error?.Message))
                    return error.Message;
            }
            catch (Exception ex)
            {
                // Fall through to default message
                System.Diagnostics.Debug.WriteLine($"[ErrorNotification] API error parse failed: {ex.Message}");
            }
        }

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => localizer["InvalidRequest"],
            HttpStatusCode.Forbidden => localizer["AccessDenied"],
            HttpStatusCode.NotFound => localizer["ResourceNotFound"],
            HttpStatusCode.Conflict => localizer["ConflictingOperation"],
            HttpStatusCode.InternalServerError => localizer["UnexpectedServerError"],
            _ => string.Format(localizer["RequestFailed"], (int)response.StatusCode)
        };
    }
}
