// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Middleware;

/// <summary>
/// Recette R-457: the requests that reached no route of the application and were refused (the 404, 401
/// and 429 of robots probing <c>/.env</c>, <c>/wp-json</c>, <c>/api/graphql</c>...) are counted instead
/// of each writing a warning line: about 32,700 of the 32,772 lines of one day were theirs. One line per
/// minute that saw any says how many there were: a warning from <see cref="PeakPerMinute"/> requests on,
/// so a flood reaches the warnings, an information line below it.
///
/// The minute is closed by the first unmatched request of a later minute: a quiet spell delays the line,
/// which then states the exact minute it covers.
/// </summary>
public sealed class UnmatchedRequestCounter(TimeProvider time, ILogger<UnmatchedRequestCounter> logger)
{
    /// <summary>
    /// Ten refused requests per second, sustained over a minute: a flood. Recette R-489: the former
    /// threshold of 60 was the ordinary rate of the robots that scan any public host (production, 30/09:
    /// 23 minutes above it in three hours, 95 requests in the busiest), so it warned about the expected.
    /// </summary>
    public const int PeakPerMinute = 600;

    private readonly Lock _gate = new();
    private DateTime _minute;
    private int _count;

    public void Record()
    {
        var now = time.GetUtcNow().UtcDateTime;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        (DateTime Minute, int Count)? closed = null;
        lock (_gate)
        {
            if (minute != _minute)
            {
                if (_count > 0)
                    closed = (_minute, _count);
                _minute = minute;
                _count = 0;
            }
            _count++;
        }

        if (closed is { } done)
            logger.Log(
                done.Count >= PeakPerMinute ? LogLevel.Warning : LogLevel.Information,
                "{Count} refused requests to no route of the application during the minute starting {Minute:O}",
                done.Count,
                done.Minute);
    }
}
