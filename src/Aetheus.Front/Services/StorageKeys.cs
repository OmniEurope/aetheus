// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Services;

/// <summary>
/// S-TECH-RB4N: the browser localStorage keys, all derived from <see cref="AppConstants.LocalStoragePrefix"/>
/// so a rebrand changes the prefix in one place instead of chasing string literals across pages.
/// </summary>
internal static class StorageKeys
{
    private const string P = AppConstants.LocalStoragePrefix;

    public const string Theme = P + "theme";
    public const string Lang = P + "lang";
    public const string DisplayName = P + "display_name";
    public const string NotifEmail = P + "notif_email";
    public const string NotifPipeline = P + "notif_pipeline";
    public const string NotifServer = P + "notif_server";
    public const string AuthToken = P + "auth_token";
    public const string RefreshToken = P + "refresh_token";
}
