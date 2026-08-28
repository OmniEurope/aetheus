// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisReportAdmissionGateTests
{
    [Fact]
    public async Task Filter_BoundsExecutionBeforeModelBinding()
    {
        var gate = Gate(maximum: 1);
        var firstFilter = new AnalysisReportAdmissionFilter(gate);
        var secondFilter = new AnalysisReportAdmissionFilter(gate);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = firstFilter.OnResourceExecutionAsync(Context(), async () =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
            return ExecutedContext();
        });
        await firstEntered.Task;

        var second = secondFilter.OnResourceExecutionAsync(Context(), () =>
        {
            secondEntered = true;
            return Task.FromResult(ExecutedContext());
        });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(secondEntered);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task Filter_ReleasesAdmissionWhenPipelineFails()
    {
        var gate = Gate(maximum: 1);
        var filter = new AnalysisReportAdmissionFilter(gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            filter.OnResourceExecutionAsync(
                Context(),
                () => throw new InvalidOperationException("model binding failed")));

        var called = false;
        await filter.OnResourceExecutionAsync(Context(), () =>
        {
            called = true;
            return Task.FromResult(ExecutedContext());
        });

        Assert.True(called);
    }

    [Fact]
    public void PublishReport_UsesAdmissionBeforeModelBinding()
    {
        var method = typeof(AnalysisController).GetMethod(nameof(AnalysisController.PublishReport));
        var attribute = Assert.Single(method!.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.ServiceFilterAttribute),
            inherit: true).Cast<Microsoft.AspNetCore.Mvc.ServiceFilterAttribute>());

        Assert.Equal(typeof(AnalysisReportAdmissionFilter), attribute.ServiceType);
    }

    private static AnalysisReportAdmissionGate Gate(int maximum) =>
        new(Options.Create(new AnalysisPlatformOptions
        {
            MaxConcurrentIngestions = Math.Max(4, maximum),
            MaxConcurrentReportAdmissions = maximum
        }));

    private static ResourceExecutingContext Context()
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
        return new ResourceExecutingContext(actionContext, [], []);
    }

    private static ResourceExecutedContext ExecutedContext() =>
        new(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new ActionDescriptor()),
            []);
}
