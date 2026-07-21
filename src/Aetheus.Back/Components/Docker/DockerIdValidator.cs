// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Docker;

public static partial class DockerIdValidator
{
    // Docker container/image IDs: hex, 12 or 64 chars
    [GeneratedRegex(@"^[a-fA-F0-9]{12,64}$")]
    private static partial Regex DockerIdRegex();

    // Strict sha256 digest: exactly 64 hex chars after the prefix.
    [GeneratedRegex(@"^sha256:[a-fA-F0-9]{64}$")]
    private static partial Regex Sha256DigestRegex();

    // Docker image names: alphanum, dots, dashes, slashes, colons (tags), underscores
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-/:@]{0,199}$")]
    private static partial Regex DockerImageNameRegex();

    // Compose stack / network / volume names: alphanum, dashes, underscores
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,199}$")]
    private static partial Regex DockerNameRegex();

    public static bool IsValidContainerId(string id) =>
        !string.IsNullOrWhiteSpace(id) && DockerIdRegex().IsMatch(id);

    public static bool IsValidImageId(string id) =>
        !string.IsNullOrWhiteSpace(id) && (DockerIdRegex().IsMatch(id) || Sha256DigestRegex().IsMatch(id));

    public static bool IsValidImageName(string name) =>
        !string.IsNullOrWhiteSpace(name) && DockerImageNameRegex().IsMatch(name);

    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && DockerNameRegex().IsMatch(name);

    // Container path allow-list: absolute, no shell metas, no traversal segments.
    [GeneratedRegex(@"^/[a-zA-Z0-9_./\-]{0,1023}$")]
    private static partial Regex ContainerPathRegex();

    public static bool IsValidContainerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!ContainerPathRegex().IsMatch(path)) return false;
        // Disallow ".." segments anywhere in the path (regex above allows dots elsewhere).
        return !path.Split('/').Any(s => s == "..");
    }
}
