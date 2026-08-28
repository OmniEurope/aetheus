// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Executors;

public static class SudoProcessStartInfo
{
    public static ProcessStartInfo Create() => new()
    {
        FileName = "sudo",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
}
