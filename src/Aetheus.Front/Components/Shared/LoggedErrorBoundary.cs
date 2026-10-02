// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Web;

namespace Aetheus.Front.Components.Shared;

public sealed class LoggedErrorBoundary : ErrorBoundary
{
    [Inject] private ClientErrorReporter Reporter { get; set; } = default!;

    public string? CorrelationId { get; private set; }

    protected override Task OnErrorAsync(Exception exception)
    {
        CorrelationId = Guid.NewGuid().ToString("N");
        Reporter.Report(
            CorrelationId,
            $"Unhandled client exception: {exception.GetType().Name}",
            exception.Message);
        return Task.CompletedTask;
    }

    public new void Recover()
    {
        CorrelationId = null;
        base.Recover();
    }
}
