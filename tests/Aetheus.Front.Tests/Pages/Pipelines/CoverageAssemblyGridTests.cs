// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Shared.DTOs;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class CoverageAssemblyGridTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public CoverageAssemblyGridTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public async Task RendersAssemblyCoverageAndExactLineTotals()
    {
        _handler.SetJsonResponse("api/pipelines/runs/9/coverage/assemblies",
            new PaginatedResult<CoverageAssemblyDto>
            {
                Items =
                [
                    new CoverageAssemblyDto
                    {
                        Name = "Aetheus.Back", LineRate = 0.75, LinesCovered = 150, LinesValid = 200
                    }
                ],
                TotalCount = 13,
                Page = 2,
                PageSize = 12
            });
        var cut = Render<CoverageAssemblyGrid>(parameters => parameters.Add(component => component.RunId, 9));
        await LoadAsync(cut, new LoadDataArgs { Skip = 12, Top = 12, OrderBy = "Name desc" });

        Assert.Contains("CoverageByAssembly", cut.Markup);
        Assert.Contains("Aetheus.Back", cut.Markup);
        Assert.Contains("75", cut.Markup);
        Assert.Contains("150 / 200", cut.Markup);
        Assert.Contains(_handler.Requests, request => request.Url.Contains("page=2", StringComparison.Ordinal));
        Assert.Contains(_handler.Requests, request => request.Url.Contains("sortBy=Name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyPage_RendersGridWithoutRows()
    {
        _handler.SetJsonResponse("api/pipelines/runs/9/coverage/assemblies",
            new PaginatedResult<CoverageAssemblyDto>());
        var cut = Render<CoverageAssemblyGrid>(parameters => parameters.Add(component => component.RunId, 9));
        await LoadAsync(cut, new LoadDataArgs { Skip = 0, Top = 12 });

        Assert.Contains("CoverageByAssembly", cut.Markup);
        Assert.Empty(cut.FindAll(".rz-data-row"));
    }

    private static async Task LoadAsync(
        IRenderedComponent<CoverageAssemblyGrid> cut, LoadDataArgs args)
    {
        var method = typeof(CoverageAssemblyGrid).GetMethod(
            "LoadDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [args])!);
        cut.Render();
    }
}
