// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Pages.Git;

internal static class GitLoadFailureLogger
{
    public static void Log(
        ILogger logger,
        Exception exception,
        string resource,
        bool detailPage)
    {
        var fallback = detailPage ? "detail fallback state" : "empty fallback";
        if (exception is JsonException)
        {
            logger.LogWarning(
                exception,
                "The Git {Resource} API returned a malformed JSON payload; the page is using its {Fallback}.",
                resource,
                fallback);
            return;
        }

        logger.LogDebug(
            exception,
            "Could not load Git {Resource}; the page is using its {Fallback}.",
            resource,
            fallback);
    }
}
