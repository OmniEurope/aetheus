// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

/// <summary>S-UX-SPKL: the availability sparkline renders one bar per sample, coloured up/down.</summary>
public sealed class AvailabilitySparklineTests : BunitContext
{
    public AvailabilitySparklineTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void RendersBar_PerSample_ColouredByStatus()
    {
        var samples = new List<AppHealthSampleDto>
        {
            new() { Timestamp = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc), IsUp = true },
            new() { Timestamp = new DateTime(2026, 1, 10, 11, 0, 0, DateTimeKind.Utc), IsUp = false },
        };

        var cut = Render<AvailabilitySparkline>(p => p.Add(c => c.Samples, samples));

        Assert.Equal(2, cut.FindAll("rect").Count);
        Assert.Contains("var(--rz-success)", cut.Markup);
        Assert.Contains("var(--rz-danger)", cut.Markup);
    }

    [Fact]
    public void RendersPlaceholder_WhenNoSamples()
    {
        var cut = Render<AvailabilitySparkline>(p => p.Add(c => c.Samples, new List<AppHealthSampleDto>()));

        Assert.Empty(cut.FindAll("rect"));
        Assert.Contains("NotAvailable", cut.Markup); // stub localizer echoes the key
    }

    [Fact]
    public void DownSampledWindow_RemainsRedWhenAnyProbeFailed()
    {
        var samples = Enumerable.Range(0, 96).Select(index => new AppHealthSampleDto
        {
            Timestamp = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
            IsUp = index != 1
        }).ToList();

        var cut = Render<AvailabilitySparkline>(parameters => parameters.Add(component => component.Samples, samples));

        Assert.Equal(48, cut.FindAll("rect").Count);
        Assert.Contains("var(--rz-danger)", cut.Markup, StringComparison.Ordinal);
    }
}
