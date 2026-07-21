// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Executors;

public static class ServerQueryHelper
{
    private const string CredentialPath = "/opt/aetheus-agent/teamspeak/query-credentials";

    // S-TECH-87: the legacy nc/stdin invocation builders were retired - the read path is now
    // TcpTeamspeakQueryClient and the write path the typed ServerQuery op. Only the credential
    // path + escaping/parsing helpers below remain in use.
    public static string ReadCredentialPath() => CredentialPath;

    public static string GetValue(string line, string key)
    {
        var prefix = key + "=";
        var startIndex = line.IndexOf(prefix, StringComparison.Ordinal);
        if (startIndex < 0) return string.Empty;

        startIndex += prefix.Length;
        var endIndex = line.IndexOf(' ', startIndex);
        return endIndex < 0 ? line[startIndex..] : line[startIndex..endIndex];
    }

    public static string EscapeServerQuery(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("/", "\\/")
            .Replace(" ", "\\s")
            .Replace("|", "\\p")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

    public static string UnescapeServerQuery(string value) =>
        value
            .Replace("\\s", " ")
            .Replace("\\p", "|")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\/", "/")
            .Replace("\\\\", "\\");
}
