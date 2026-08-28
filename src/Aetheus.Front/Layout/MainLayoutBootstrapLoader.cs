// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

internal static class MainLayoutBootstrapLoader
{
    internal readonly record struct SessionBootstrapResult(
        Microsoft.AspNetCore.SignalR.Client.HubConnectionState SignalRState,
        bool WasConnected,
        bool BackendLive);

    public static async Task LoadPermissionsAsync(
        ApiClient api,
        PermissionService permissions,
        AuthStateProvider auth,
        ILogger logger)
    {
        try
        {
            var summary = await api.Auth.GetMyPermissionsAsync();
            if (summary is not null)
                permissions.SetPermissions(summary.EffectivePermissions, auth.IsAdmin);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[MainLayout] Permissions load failed");
        }
    }

    public static async Task LoadOrganizationsAsync(
        ActiveOrganizationService organizations,
        ILogger logger)
    {
        try
        {
            await organizations.RefreshAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[MainLayout] Organizations load failed");
        }
    }

    public static async Task<SessionBootstrapResult> StartAuthenticatedSessionAsync(
        ApiClient api,
        PermissionService permissions,
        AuthStateProvider auth,
        ActiveOrganizationService organizations,
        TaskTrackerService taskTracker,
        ILogger logger)
    {
        var bootstrap = new List<Task>(2);
        if (!permissions.IsLoaded) bootstrap.Add(LoadPermissionsAsync(api, permissions, auth, logger));
        if (!organizations.IsLoaded) bootstrap.Add(LoadOrganizationsAsync(organizations, logger));
        if (bootstrap.Count > 0) await Task.WhenAll(bootstrap);

        try { await taskTracker.StartAsync(); }
        catch (Exception exception) { logger.LogWarning(exception, "[MainLayout] TaskTracker start after login failed"); }
        var state = taskTracker.ConnectionState;
        var connected = state == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
        return new(state, connected, connected || await api.Auth.IsBackendLiveAsync());
    }
}
