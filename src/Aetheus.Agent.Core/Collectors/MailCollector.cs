// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Agent.Core.Collectors;

public sealed partial class MailCollector(
    ILogger<MailCollector> logger,
    IShellRunner shell,
    Func<string, bool>? fileExists = null)
    : BaseShellCollector<MailCollector>(logger, shell), IMailCollector
{
    private const string PostfixMainConfigPath = "/etc/postfix/main.cf";

    public async Task<MailDataDto> CollectAsync(CancellationToken ct = default)
    {
        var hasConfiguredPostfix = false;
        try
        {
            var hasPostfixBinary = await DetectBinaryAsync("postfix", ct).ConfigureAwait(false);
            if (!hasPostfixBinary)
                return new MailDataDto { IsInstalled = false };

            var postfixVersion = await TryCollectConfiguredPostfixVersionAsync(ct).ConfigureAwait(false);
            if (postfixVersion is null)
                return new MailDataDto { IsInstalled = false };

            hasConfiguredPostfix = true;
            var hasDovecot = await DetectBinaryAsync("dovecot", ct).ConfigureAwait(false);

            var dovecotVersionTask = hasDovecot ? CollectDovecotVersionAsync(ct) : Task.FromResult(string.Empty);
            var postfixRunningTask = CollectServiceRunningAsync("postfix", ct);
            var dovecotRunningTask = hasDovecot ? CollectServiceRunningAsync("dovecot", ct) : Task.FromResult(false);
            var queueTask = CollectQueueSizeAsync(ct);
            var domainsTask = CollectDomainsAsync(ct);

            await Task.WhenAll(dovecotVersionTask, postfixRunningTask, dovecotRunningTask, queueTask, domainsTask)
                .ConfigureAwait(false);

            return new MailDataDto
            {
                IsInstalled = true,
                IsPostfixRunning = await postfixRunningTask.ConfigureAwait(false),
                IsDovecotRunning = await dovecotRunningTask.ConfigureAwait(false),
                PostfixVersion = postfixVersion,
                DovecotVersion = await dovecotVersionTask.ConfigureAwait(false),
                QueueSize = await queueTask.ConfigureAwait(false),
                Domains = await domainsTask.ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            // F-ENG-06: a partial failure after a configured postfix was found must not report mail as uninstalled,
            // and must be visible (LogWarning), not hidden in Debug.
            Logger.LogWarning(ex, hasConfiguredPostfix
                ? "Mail data collection failed after validating postfix; reporting installed"
                : "Mail detection failed");
            return new MailDataDto { IsInstalled = hasConfiguredPostfix };
        }
    }

    private async Task<bool> DetectBinaryAsync(string binary, CancellationToken ct)
    {
        var (locator, target) = OperatingSystem.IsWindows()
            ? ("where", $"{binary}.exe")
            : ("which", binary);
        var res = await Shell.RunExecAsync(locator, [target], ct).ConfigureAwait(false);
        return res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StdOut);
    }

    private async Task<string?> TryCollectConfiguredPostfixVersionAsync(CancellationToken ct)
    {
        if (!(fileExists ?? File.Exists)(PostfixMainConfigPath))
        {
            Logger.LogDebug(
                "Postfix binary found but {ConfigPath} is absent; skipping mail collection",
                PostfixMainConfigPath);
            return null;
        }

        try
        {
            var res = await Shell.RunExecAsync("postconf", ["mail_version"], ct).ConfigureAwait(false);
            if (res.ExitCode != 0)
            {
                Logger.LogDebug(
                    "Postfix configuration validation failed with exit code {ExitCode}; skipping mail collection",
                    res.ExitCode);
                return null;
            }

            var match = PostfixVersionRegex().Match(res.StdOut);
            if (match.Success)
                return match.Groups[1].Value;

            Logger.LogDebug("Postfix configuration validation returned no mail version; skipping mail collection");
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Postfix configuration validation failed; skipping mail collection");
            return null;
        }
    }

    private async Task<string> CollectDovecotVersionAsync(CancellationToken ct)
    {
        try
        {
            var res = await Shell.RunExecAsync("dovecot", ["--version"], ct).ConfigureAwait(false);
            var output = res.StdOut;
            var match = DovecotVersionRegex().Match(output);
            return match.Success ? match.Groups[1].Value : output.Trim().Split(' ')[0];
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get Dovecot version");
            return string.Empty;
        }
    }

    private async Task<bool> CollectServiceRunningAsync(string serviceName, CancellationToken ct)
    {
        try
        {
            var res = await Shell.RunExecAsync("systemctl", ["is-active", serviceName], ct).ConfigureAwait(false);
            return res.StdOut.Trim().Equals("active", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to check {Service} running state", serviceName);
            return false;
        }
    }

    private async Task<int> CollectQueueSizeAsync(CancellationToken ct)
    {
        try
        {
            var res = await Shell.RunExecAsync("postqueue", ["-p"], ct).ConfigureAwait(false);
            if (res.StdOut.Contains("Mail queue is empty", StringComparison.OrdinalIgnoreCase))
                return 0;

            // Was `postqueue -p | tail -n 1` - take the last non-empty line in-process.
            var lastLine = res.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault() ?? string.Empty;

            var match = QueueSizeRegex().Match(lastLine);
            return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : 0;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to collect mail queue size");
            return 0;
        }
    }

    private async Task<List<MailDomainDto>> CollectDomainsAsync(CancellationToken ct)
    {
        var result = new List<MailDomainDto>();
        try
        {
            var res = await Shell.RunExecAsync("postconf", ["-h", "virtual_mailbox_domains"], ct).ConfigureAwait(false);
            var output = res.StdOut;
            if (string.IsNullOrWhiteSpace(output))
                return result;

            var domains = output.Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var domain in domains)
            {
                if (string.IsNullOrWhiteSpace(domain) || domain.StartsWith('$'))
                    continue;

                result.Add(new MailDomainDto
                {
                    Name = domain.Trim(),
                    IsActive = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to collect mail domains");
        }
        return result;
    }

    // "mail_version = 3.8.1"
    [GeneratedRegex(@"mail_version\s*=\s*([\d.]+)")]
    private static partial Regex PostfixVersionRegex();

    // "2.3.21 (abc123)"
    [GeneratedRegex(@"^([\d.]+)")]
    private static partial Regex DovecotVersionRegex();

    // "-- 5 Kbytes in 3 Requests."
    [GeneratedRegex(@"(\d+)\s+Request")]
    private static partial Regex QueueSizeRegex();
}
