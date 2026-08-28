// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Json;

namespace Aetheus.Front.Services;

public sealed class ClientErrorReporter(HttpClient http, NavigationManager navigation)
{
    public void Report(string correlationId, string summary, string message)
    {
        _ = ReportAsync(correlationId, summary, message);
    }

    private async Task ReportAsync(string correlationId, string summary, string message)
    {
        try
        {
            await http.PostAsJsonAsync("api/client-errors", new ClientErrorLogRequest
            {
                CorrelationId = correlationId,
                Summary = Truncate(summary, 200),
                Message = Truncate(message, 2000),
                Path = Truncate(navigation.ToBaseRelativePath(navigation.Uri), 500)
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ClientErrorReporter] report failed: {ex.Message}");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
