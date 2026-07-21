// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Portsentry;

public static partial class PortsentryCommandHelper
{
    // All shell-string builders were removed: portsentry now dispatches exclusively via typed
    // OperationKind ops (PortsentrySetup / PortsentryUnblock / service + logs + status), each argv-exact
    // through a root-owned helper or the systemd sandbox. Mode/port-list validation lives in the shared
    // PortsentryValidation (used by OperationTargetValidator and re-checked agent-side). Only the request
    // guard IsValidIpAddress remains - it still gates PortsentryService block/unblock request validation.

    public static bool IsValidIpAddress(string ip) =>
        !string.IsNullOrWhiteSpace(ip) && IpAddressRegex().IsMatch(ip);

    [GeneratedRegex(@"^[0-9a-fA-F.:]+$")]
    private static partial Regex IpAddressRegex();
}
