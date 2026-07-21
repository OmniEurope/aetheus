// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Collectors;

public sealed partial class MailCollector(ILogger<MailCollector> logger, IShellRunner shell)
    : BaseShellCollector<MailCollector>(logger, shell), IMailCollector
{
    public async Task<MailDataDto> CollectAsync(CancellationToken ct = default)
    {
        var hasPostfix = false;
        try
        {
            hasPostfix = await DetectBinaryAsync("postfix", ct).ConfigureAwait(false);
            if (!hasPostfix)
                return new MailDataDto { IsInstalled = false };

            var hasDovecot = await DetectBinaryAsync("dovecot", ct).ConfigureAwait(false);

            var postfixVersionTask = CollectPostfixVersionAsync(ct);
            var dovecotVersionTask = hasDovecot ? CollectDovecotVersionAsync(ct) : Task.FromResult(string.Empty);
            var postfixRunningTask = CollectServiceRunningAsync("postfix", ct);
            var dovecotRunningTask = hasDovecot ? CollectServiceRunningAsync("dovecot", ct) : Task.FromResult(false);
            var queueTask = CollectQueueSizeAsync(ct);
            var domainsTask = CollectDomainsAsync(ct);

            await Task.WhenAll(postfixVersionTask, dovecotVersionTask, postfixRunningTask, dovecotRunningTask, queueTask, domainsTask)
                .ConfigureAwait(false);

            return new MailDataDto
            {
                IsInstalled = true,
                IsPostfixRunning = await postfixRunningTask.ConfigureAwait(false),
                IsDovecotRunning = await dovecotRunningTask.ConfigureAwait(false),
                PostfixVersion = await postfixVersionTask.ConfigureAwait(false),
                DovecotVersion = await dovecotVersionTask.ConfigureAwait(false),
                QueueSize = await queueTask.ConfigureAwait(false),
                Domains = await domainsTask.ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            // F-ENG-06: a partial failure after postfix was found must not report mail as uninstalled,
            // and must be visible (LogWarning), not hidden in Debug.
            Logger.LogWarning(ex, hasPostfix
                ? "Mail data collection failed after detecting postfix; reporting installed"
                : "Mail detection failed");
            return new MailDataDto { IsInstalled = hasPostfix };
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

    private async Task<string> CollectPostfixVersionAsync(CancellationToken ct)
    {
        try
        {
            var res = await Shell.RunExecAsync("postconf", ["mail_version"], ct).ConfigureAwait(false);
            var match = PostfixVersionRegex().Match(res.StdOut);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get Postfix version");
            return string.Empty;
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
