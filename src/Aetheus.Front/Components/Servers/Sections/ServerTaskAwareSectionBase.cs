// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public abstract class ServerTaskAwareSectionBase : ServerLoaderSectionBase, IDisposable
{
    private ServerDetailLoader? _subscribedLoader;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (Loader is null || ReferenceEquals(Loader, _subscribedLoader)) return;

        if (_subscribedLoader is not null)
            _subscribedLoader.OnTaskCompleted -= OnTaskCompletedAsync;
        Loader.OnTaskCompleted += OnTaskCompletedAsync;
        _subscribedLoader = Loader;
    }

    protected abstract Task HandleTaskCompletedNotificationAsync(TaskCompletedNotification notification);

    public void Dispose()
    {
        if (_subscribedLoader is not null)
            _subscribedLoader.OnTaskCompleted -= OnTaskCompletedAsync;
        GC.SuppressFinalize(this);
    }

    private Task OnTaskCompletedAsync(TaskCompletedNotification notification)
        => HandleTaskCompletedNotificationAsync(notification);
}
