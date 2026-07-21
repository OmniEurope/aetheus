// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Tests.TestDoubles;

internal sealed class ImmediateDialogService(NavigationManager nav, IJSRuntime js) : DialogService(nav, js)
{
    public int OpenCount { get; private set; }
    public Type? LastComponent { get; private set; }
    public string? LastTitle { get; private set; }
    public Dictionary<string, object?>? LastParameters { get; private set; }
    public object? OpenResult { get; set; }
    public bool? ConfirmResult { get; set; } = false;
    public string? LastConfirmMessage { get; private set; }

    public override Task<dynamic?> OpenAsync<T>(string title, Dictionary<string, object?>? parameters = null, DialogOptions? options = null)
    {
        OpenCount++;
        LastComponent = typeof(T);
        LastTitle = title;
        LastParameters = parameters;
        return Task.FromResult<dynamic?>(OpenResult);
    }

    public override Task<bool?> Confirm(string message, string title, ConfirmOptions? options = null, CancellationToken? cancellationToken = null)
    {
        OpenCount++;
        LastTitle = title;
        LastConfirmMessage = message;
        return Task.FromResult(ConfirmResult);
    }
}
