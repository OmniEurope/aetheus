// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>Canonical local-development endpoints shared by every Aetheus runtime.</summary>
public static class LocalDevelopmentEndpoints
{
    public const string ApiHttpsBaseUrl = "https://localhost:5301";
    public const string ApiHttpBaseUrl = "http://localhost:5300";
    public const string CliApiBaseUrl = ApiHttpsBaseUrl;
    public const string FrontendHttpsOrigin = "https://localhost:5401";
    public const string FrontendHttpOrigin = "http://localhost:5401";
}
