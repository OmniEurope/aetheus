// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using OpenTelemetry;

namespace Aetheus.Telemetry;

internal sealed class SensitiveActivityProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> ForbiddenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "url.full",
        "url.query",
        "url.fragment",
        "http.url",
        "http.target",
        "http.request.body",
        "http.response.body",
        "db.statement",
        "db.query.text",
        "enduser.id",
        "user.id",
        "user.email",
        "exception.message",
        "exception.stacktrace"
    };

    public override void OnEnd(Activity activity)
    {
        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (IsForbidden(tag.Key))
                activity.SetTag(tag.Key, null);
        }
    }

    private static bool IsForbidden(string key) =>
        ForbiddenTags.Contains(key)
        || key.StartsWith("http.request.header.", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("http.response.header.", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("db.query.", StringComparison.OrdinalIgnoreCase);
}
