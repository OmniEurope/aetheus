// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Back.Components.Docker;

public static class DockerCommandHelper
{
    public static string EncodeBase64Shell(string input)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        return $"printf '%s' '{base64}' | base64 -d";
    }
}
