// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public static class DisplayFormatting
{
    public static BadgeStyle TaskLogBadge(TaskLogLevel level) => level switch
    {
        TaskLogLevel.Error => BadgeStyle.Danger,
        TaskLogLevel.Warning => BadgeStyle.Warning,
        TaskLogLevel.Info => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    public static string Size(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };

    public static string ShortSha(string commitHash) =>
        commitHash[..Math.Min(8, commitHash.Length)];

    public static string BinarySize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
