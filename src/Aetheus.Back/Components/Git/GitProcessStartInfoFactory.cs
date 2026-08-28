// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Text;

namespace Aetheus.Back.Components.Git;

internal static class GitProcessStartInfoFactory
{
    public static ProcessStartInfo Create(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
