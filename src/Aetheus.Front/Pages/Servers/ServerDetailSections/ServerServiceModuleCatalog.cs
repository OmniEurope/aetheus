// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

internal static class ServerServiceModuleCatalog
{
    private static readonly Dictionary<string, string> ServiceToModule = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docker"] = "docker",
        ["apache2"] = "apache",
        ["postfix"] = "mail",
        ["dovecot"] = "mail",
        ["portsentry"] = "portsentry",
        ["rkhunter"] = "rkhunter",
        ["certbot"] = "certbot",
        ["teamspeak"] = "teamspeak",
        ["ts3server"] = "teamspeak",
        ["cron"] = "cron",
        ["crond"] = "cron"
    };

    private static readonly (string DisplayName, string Module)[] DedicatedModules =
    [
        ("docker", "docker"),
        ("apache2", "apache"),
        ("certbot", "certbot"),
        ("postfix", "mail"),
        ("teamspeak", "teamspeak"),
        ("portsentry", "portsentry"),
        ("rkhunter", "rkhunter")
    ];

    public static bool Contains(string serviceName) => ServiceToModule.ContainsKey(serviceName);

    public static bool TryGetSection(string serviceName, out string section)
    {
        if (ServiceToModule.TryGetValue(serviceName, out var mapped))
        {
            section = mapped;
            return true;
        }
        section = string.Empty;
        return false;
    }

    public static IEnumerable<ServiceInfoDto> MissingFrom(IReadOnlyCollection<ServiceInfoDto> services) =>
        DedicatedModules
            .Where(module => !services.Any(service =>
                ServiceToModule.TryGetValue(service.Name, out var mapped)
                && mapped == module.Module))
            .Select(module => new ServiceInfoDto
            {
                Name = module.DisplayName,
                Type = ServiceType.Systemd,
                Status = string.Empty,
                IsRunning = false,
                IsManageable = true,
                IsInstalled = false
            });
}
