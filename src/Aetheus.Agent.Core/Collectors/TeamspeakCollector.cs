// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Agent.Core.Collectors;

public sealed partial class TeamspeakCollector(ILogger<TeamspeakCollector> logger, IShellRunner shell, ITeamspeakQueryClient queryClient, Func<string, bool>? fileExists = null)
    : BaseShellCollector<TeamspeakCollector>(logger, shell), ITeamspeakCollector
{
    // Injectable binary-path probe so a unit test can force "no binary on disk" deterministically. In
    // production it is File.Exists; without this seam the test reads the real host filesystem and fails
    // on any CI agent where a ts3server binary sits at one of the known paths (the VPS runner does).
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    private static readonly string[] KnownPaths =
    [
        "/opt/teamspeak3-server_linux_amd64/ts3server",
        "/opt/teamspeak/ts3server",
        "/usr/local/teamspeak/ts3server"
    ];

    private const string CredentialPath = "/opt/aetheus-agent/teamspeak/query-credentials";

    public async Task<TeamspeakDataDto> CollectAsync(CancellationToken ct = default)
    {
        try
        {
            // A locked-down install (e.g. /opt/teamspeak mode 0750 owned by the teamspeak user)
            // is unreadable by the unprivileged agent, so File.Exists on the binary returns false
            // even though TS is running. Treat a live ts3server process as proof of installation:
            // `pidof` and the listening query port need no filesystem access to the install dir.
            var isRunning = await IsRunningAsync(ct).ConfigureAwait(false);
            var binaryPath = await DetectBinaryAsync(ct).ConfigureAwait(false);

            if (binaryPath is null && !isRunning)
                return new TeamspeakDataDto();

            if (!isRunning)
                return new TeamspeakDataDto { IsInstalled = true };

            var queryPort = await DetectQueryPortAsync(binaryPath, ct).ConfigureAwait(false);
            var queryPassword = await ReadQueryPasswordAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(queryPassword))
            {
                return new TeamspeakDataDto
                {
                    IsInstalled = true,
                    IsRunning = true,
                    QueryPort = queryPort
                };
            }

            var queryResult = await RunServerQueryAsync(queryPort, queryPassword, ct).ConfigureAwait(false);
            if (queryResult is null)
            {
                return new TeamspeakDataDto
                {
                    IsInstalled = true,
                    IsRunning = true,
                    QueryPort = queryPort
                };
            }

            return ParseQueryResult(queryResult, queryPort);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to collect TeamSpeak data");
            return new TeamspeakDataDto();
        }
    }

    private async Task<string?> DetectBinaryAsync(CancellationToken ct)
    {
        foreach (var path in KnownPaths)
        {
            if (_fileExists(path))
                return path;
        }

        var res = await Shell.RunExecAsync("which", ["ts3server"], ct).ConfigureAwait(false);
        return res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StdOut) ? res.StdOut.Trim() : null;
    }

    private async Task<bool> IsRunningAsync(CancellationToken ct)
    {
        // The unit is named "teamspeak" on some installs and "teamspeak3" on others; accept both.
        foreach (var unit in new[] { "teamspeak3", "teamspeak" })
        {
            var svc = await Shell.RunExecAsync("systemctl", ["is-active", unit], ct).ConfigureAwait(false);
            if (svc.StdOut.Trim().Equals("active", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var pidof = await Shell.RunExecAsync("pidof", ["ts3server"], ct).ConfigureAwait(false);
        return pidof.ExitCode == 0 && !string.IsNullOrWhiteSpace(pidof.StdOut);
    }

    // binaryPath is null when the install dir is unreadable by the agent; fall back to the
    // ServerQuery default (10011) since ts3server.ini lives in that same unreadable dir.
    private async Task<int> DetectQueryPortAsync(string? binaryPath, CancellationToken ct)
    {
        if (binaryPath is null)
            return 10011;
        try
        {
            var dir = binaryPath[..binaryPath.LastIndexOf('/')];
            var iniPath = Path.Combine(dir, "ts3server.ini");
            if (File.Exists(iniPath))
            {
                var content = await File.ReadAllTextAsync(iniPath, ct).ConfigureAwait(false);
                var match = QueryPortRegex().Match(content);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    return port;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to detect query port");
        }
        return 10011;
    }

    private async Task<string?> ReadQueryPasswordAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(CredentialPath))
                return null;
            var password = (await File.ReadAllTextAsync(CredentialPath, ct).ConfigureAwait(false)).Trim();
            return string.IsNullOrEmpty(password) ? null : password;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> RunServerQueryAsync(int queryPort, string password, CancellationToken ct)
    {
        // S-FEAT-11: talk to ServerQuery over a native TCP socket instead of `echo … | nc`.
        // The credential and command stream never touch a shell parser, and there is no `nc`
        // dependency on the host. Escaping is still applied so a password can't break the protocol.
        var commands = $"login serveradmin {ServerQueryHelper.EscapeServerQuery(password)}\nuse sid=1\nserverinfo\nclientlist -uid -times -info\nchannellist\nbanlist\nquit\n";
        return await queryClient.ExecuteAsync(queryPort, commands, ct).ConfigureAwait(false);
    }

    private TeamspeakDataDto ParseQueryResult(string raw, int queryPort)
    {
        var dto = new TeamspeakDataDto
        {
            IsInstalled = true,
            IsRunning = true,
            QueryPort = queryPort
        };

        try
        {
            var lines = FindQueryLines(raw);
            var server = ParseServerInfo(lines.ServerInfo);
            var channels = ParseChannels(lines.Channels);
            var clients = ParseClients(lines.Clients);
            var bans = ParseBans(lines.Bans);
            var onlineClients = clients.Count(c => !c.IsServerQuery);
            dto = dto with
            {
                ServerName = server.Name,
                Version = server.Version,
                Platform = server.Platform,
                UptimeSeconds = server.UptimeSeconds,
                MaxClients = server.MaxClients,
                VoicePort = server.VoicePort,
                OnlineClients = onlineClients,
                ChannelCount = channels.Count,
                Channels = channels,
                Clients = clients,
                Bans = bans
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to parse TeamSpeak ServerQuery response");
        }

        return dto;
    }

    private static QueryLines FindQueryLines(string raw)
    {
        string? server = null, clients = null, channels = null, bans = null;
        foreach (var rawLine in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Contains("virtualserver_name=", StringComparison.Ordinal)) server = line;
            else if (line.StartsWith("clid=", StringComparison.Ordinal)) clients = line;
            else if (line.StartsWith("cid=", StringComparison.Ordinal) && line.Contains("channel_name=", StringComparison.Ordinal)) channels = line;
            else if (line.StartsWith("banid=", StringComparison.Ordinal)) bans = line;
        }
        return new QueryLines(server, clients, channels, bans);
    }

    private static ServerInfo ParseServerInfo(string? line)
    {
        if (line is null) return new ServerInfo(string.Empty, string.Empty, string.Empty, 0, 0, 0);
        _ = long.TryParse(ServerQueryHelper.GetValue(line, "virtualserver_uptime"), out var uptime);
        _ = int.TryParse(ServerQueryHelper.GetValue(line, "virtualserver_maxclients"), out var maxClients);
        _ = int.TryParse(ServerQueryHelper.GetValue(line, "virtualserver_port"), out var voicePort);
        return new ServerInfo(
            ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(line, "virtualserver_name")),
            ServerQueryHelper.GetValue(line, "virtualserver_version"),
            ServerQueryHelper.GetValue(line, "virtualserver_platform"), uptime, maxClients, voicePort);
    }

    private static List<TeamspeakChannelDto> ParseChannels(string? line) =>
        line is null ? [] : line.Split('|').Select(ParseChannel).ToList();

    private static TeamspeakChannelDto ParseChannel(string record)
    {
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "cid"), out var cid);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "pid"), out var pid);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "channel_order"), out var order);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "total_clients"), out var totalClients);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "channel_maxclients"), out var maxClients);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "channel_flag_default"), out var isDefault);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "channel_flag_password"), out var hasPassword);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "channel_flag_permanent"), out var isPermanent);
        return new TeamspeakChannelDto { Id = cid, Name = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "channel_name")), ParentId = pid, Order = order, TotalClients = totalClients, MaxClients = maxClients, IsDefault = isDefault == 1, HasPassword = hasPassword == 1, IsPermanent = isPermanent == 1 };
    }

    private static List<TeamspeakClientDto> ParseClients(string? line) =>
        line is null ? [] : line.Split('|').Select(ParseClient).ToList();

    private static TeamspeakClientDto ParseClient(string record)
    {
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "clid"), out var id);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "cid"), out var channelId);
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "client_type"), out var type);
        _ = long.TryParse(ServerQueryHelper.GetValue(record, "client_idle_time"), out var idle);
        _ = long.TryParse(ServerQueryHelper.GetValue(record, "connection_connected_time"), out var connected);
        return new TeamspeakClientDto { ClientId = id, UniqueId = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "client_unique_identifier")), Nickname = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "client_nickname")), ChannelId = channelId, Platform = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "client_platform")), Version = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "client_version")), IdleTimeSeconds = idle / 1000, ConnectionTimeSeconds = connected / 1000, IsServerQuery = type == 1 };
    }

    private static List<TeamspeakBanDto> ParseBans(string? line) =>
        line is null ? [] : line.Split('|').Select(ParseBan).ToList();

    private static TeamspeakBanDto ParseBan(string record)
    {
        _ = int.TryParse(ServerQueryHelper.GetValue(record, "banid"), out var id);
        _ = long.TryParse(ServerQueryHelper.GetValue(record, "duration"), out var duration);
        _ = long.TryParse(ServerQueryHelper.GetValue(record, "created"), out var created);
        return new TeamspeakBanDto { BanId = id, Ip = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "ip")), UniqueId = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "uid")), Nickname = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "lastnickname")), Reason = ServerQueryHelper.UnescapeServerQuery(ServerQueryHelper.GetValue(record, "reason")), Duration = duration, Created = created };
    }

    private sealed record QueryLines(string? ServerInfo, string? Clients, string? Channels, string? Bans);
    private sealed record ServerInfo(string Name, string Version, string Platform, long UptimeSeconds, int MaxClients, int VoicePort);

    [GeneratedRegex(@"query_port\s*=\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex QueryPortRegex();
}
