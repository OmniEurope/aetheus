// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Webhooks;

public class WebhookService(
    IWebhookRepository repo,
    IAuditService audit,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<WebhookService> logger,
    IEncryptionService encryption,
    TimeProvider timeProvider) : IWebhookService
{
    private const int MaxFailuresBeforeDisable = 10;

    // Cached options for the outbound webhook payload (camelCase JSON, cheap allocation reuse). The
    // payload is a server-controlled internal event object, so no custom converters are needed.
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<WebhookSubscriptionDto>> GetSubscriptionsAsync(CancellationToken ct = default)
    {
        var items = await repo.GetAllAsync(ct).ConfigureAwait(false);
        return items.Select(MapToDto).ToList();
    }

    public async Task<WebhookSubscriptionDto?> GetSubscriptionAsync(int id, CancellationToken ct = default)
    {
        var item = await repo.FindAsync(id, ct).ConfigureAwait(false);
        return item is null ? null : MapToDto(item);
    }

    public async Task<WebhookSubscriptionDto> CreateSubscriptionAsync(CreateWebhookSubscriptionRequest request, CancellationToken ct = default)
    {
        ValidateTargetUrl(request.TargetUrl);

        var subscription = new WebhookSubscription
        {
            EventType = request.EventType,
            TargetUrl = request.TargetUrl,
            Secret = request.Secret is not null ? encryption.EncryptValue(request.Secret) : null
        };

        await repo.AddAsync(subscription, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "WebhookSubscription", subscription.Id, subscription.EventType, ct).ConfigureAwait(false);
        return MapToDto(subscription);
    }

    public async Task<WebhookSubscriptionDto?> UpdateSubscriptionAsync(int id, UpdateWebhookSubscriptionRequest request, CancellationToken ct = default)
    {
        ValidateTargetUrl(request.TargetUrl);

        var subscription = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (subscription is null) return null;

        subscription.EventType = request.EventType;
        subscription.TargetUrl = request.TargetUrl;
        subscription.IsEnabled = request.IsEnabled;
        subscription.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (request.Secret is not null)
            subscription.Secret = encryption.EncryptValue(request.Secret);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "WebhookSubscription", subscription.Id, subscription.EventType, ct).ConfigureAwait(false);
        return MapToDto(subscription);
    }

    public async Task<bool> DeleteSubscriptionAsync(int id, CancellationToken ct = default)
    {
        var subscription = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (subscription is null) return false;

        await repo.RemoveAsync(subscription, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "WebhookSubscription", id, subscription.EventType, ct).ConfigureAwait(false);
        return true;
    }

    public async Task FireEventAsync(string eventType, object payload, CancellationToken ct = default)
    {
        var subscriptions = await repo.GetEnabledByEventAsync(eventType, ct).ConfigureAwait(false);
        if (subscriptions.Count == 0) return;

        var jsonPayload = JsonSerializer.Serialize(payload, PayloadJsonOptions);

        foreach (var sub in subscriptions)
        {
            // Re-validate at fire time - the target may have been written before SSRF guards were added,
            // and DNS may resolve to a private address even if the literal hostname looks public.
            if (!await IsTargetUrlSafeAsync(sub.TargetUrl, ct).ConfigureAwait(false))
            {
                logger.LogWarning("Webhook {Id} skipped: target {Url} resolves to a forbidden address", sub.Id, sub.TargetUrl);
                sub.FailureCount++;
                if (sub.FailureCount >= MaxFailuresBeforeDisable)
                    sub.IsEnabled = false;
                await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                using var client = httpClientFactory.CreateClient("webhooks");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, sub.TargetUrl);
                httpRequest.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                httpRequest.Headers.Add("X-Aetheus-Event", eventType);

                if (!string.IsNullOrEmpty(sub.Secret))
                {
                    // Secrets are encrypted at rest; any residual plaintext was converted by the
                    // one-shot WebhookSecretReencryption startup pass, so decrypt strictly here. An
                    // undecryptable secret (e.g. written under a since-rotated Auth:EncryptionKey)
                    // must not abort delivery to every other subscription - isolate the failure.
                    string decryptedSecret;
                    try
                    {
                        decryptedSecret = encryption.DecryptValue(sub.Secret);
                    }
                    catch (Exception ex) when (ex is CryptographicException or FormatException)
                    {
                        logger.LogWarning(ex, "Webhook {Id} secret could not be decrypted; skipping delivery.", sub.Id);
                        sub.FailureCount++;
                        if (sub.FailureCount >= MaxFailuresBeforeDisable)
                            sub.IsEnabled = false;
                        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                        continue;
                    }
                    var signature = WebhookSignatureValidator.Compute(decryptedSecret, jsonPayload);
                    httpRequest.Headers.Add("X-Aetheus-Signature", $"sha256={signature}");
                }

                var response = await client.SendAsync(httpRequest, ct).ConfigureAwait(false);

                sub.LastTriggeredAt = timeProvider.GetUtcNow().UtcDateTime;

                if (response.IsSuccessStatusCode)
                {
                    sub.FailureCount = 0;
                }
                else
                {
                    var status = (int)response.StatusCode;
                    var isPermanent = status is >= 400 and < 500 && status != 408 && status != 429;
                    if (isPermanent)
                    {
                        sub.FailureCount++;
                        logger.LogWarning(
                            "Webhook {Id} to {Url} returned permanent failure {StatusCode}",
                            sub.Id, sub.TargetUrl, response.StatusCode);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Webhook {Id} to {Url} returned transient failure {StatusCode}",
                            sub.Id, sub.TargetUrl, response.StatusCode);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // Treat network-level errors as transient - do not count toward auto-disable.
                logger.LogWarning(ex, "Webhook {Id} to {Url} failed (transient network error)", sub.Id, sub.TargetUrl);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // Timeout - transient.
                logger.LogWarning(ex, "Webhook {Id} to {Url} timed out (transient)", sub.Id, sub.TargetUrl);
            }

            if (sub.FailureCount >= MaxFailuresBeforeDisable)
            {
                sub.IsEnabled = false;
                logger.LogWarning("Webhook {Id} disabled after {Count} consecutive failures", sub.Id, sub.FailureCount);
            }

            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronous URL-shape validation invoked when the subscription is saved. Rejects schemes
    /// other than http/https, IP literals that point at loopback / RFC1918 / link-local ranges,
    /// and hostnames that explicitly resolve to such addresses.
    /// </summary>
    private void ValidateTargetUrl(string targetUrl)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new BadRequestException("Webhook target must be an absolute http(s) URL.");

        // Allow operators to opt private targets in (e.g. internal alerting endpoints).
        var allowPrivate = config.GetValue("Webhooks:AllowPrivateTargets", false);
        if (allowPrivate) return;

        if (IPAddress.TryParse(uri.Host, out var literal) && IsForbiddenAddress(literal))
            throw new BadRequestException("Webhook target points to a forbidden address.");

        // Hostnames are re-validated at fire time via IsTargetUrlSafeAsync (DNS lookup).
    }

    /// <summary>
    /// Async fire-time validation. Resolves the host and rejects any address that falls into a
    /// loopback / private / link-local / multicast range, mitigating SSRF against internal services.
    /// </summary>
    private async Task<bool> IsTargetUrlSafeAsync(string targetUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        if (config.GetValue("Webhooks:AllowPrivateTargets", false)) return true;

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return false;
            }
        }

        return addresses.Length > 0 && !addresses.Any(IsForbiddenAddress);
    }

    private static bool IsForbiddenAddress(IPAddress address)
        => WebhookSsrfGuard.IsForbiddenAddress(address);

    private static WebhookSubscriptionDto MapToDto(WebhookSubscription w) => new()
    {
        Id = w.Id,
        EventType = w.EventType,
        TargetUrl = w.TargetUrl,
        HasSecret = !string.IsNullOrEmpty(w.Secret),
        IsEnabled = w.IsEnabled,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt,
        LastTriggeredAt = w.LastTriggeredAt,
        FailureCount = w.FailureCount
    };
}
