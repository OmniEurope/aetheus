// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Radzen;

namespace Aetheus.Front.Tests.Helpers;

public class LoadDataArgsExtensionsTests
{
    [Fact]
    public void GetPage_NoSkipNoTop_ReturnsPage1()
    {
        var args = new LoadDataArgs();
        Assert.Equal(1, args.GetPage());
    }

    [Fact]
    public void GetPage_WithSkipAndTop_CalculatesCorrectly()
    {
        var args = new LoadDataArgs { Skip = 50, Top = 25 };
        Assert.Equal(3, args.GetPage());
    }

    [Fact]
    public void GetPage_Skip0Top10_ReturnsPage1()
    {
        var args = new LoadDataArgs { Skip = 0, Top = 10 };
        Assert.Equal(1, args.GetPage());
    }

    [Fact]
    public void GetPage_TopZero_FallsBackToDefault()
    {
        var args = new LoadDataArgs { Skip = 0, Top = 0 };
        Assert.Equal(1, args.GetPage());
    }

    [Fact]
    public void GetPageSize_NoTop_ReturnsDefault()
    {
        var args = new LoadDataArgs();
        Assert.Equal(LoadDataArgsExtensions.DefaultPageSize, args.GetPageSize());
    }

    [Fact]
    public void GetPageSize_WithTop_ReturnsTop()
    {
        var args = new LoadDataArgs { Top = 50 };
        Assert.Equal(50, args.GetPageSize());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(500, 200)]
    public void GetPageSize_OutOfRange_IsClamped(int requested, int expected)
    {
        var args = new LoadDataArgs { Top = requested };
        Assert.Equal(expected, args.GetPageSize());
    }

    [Fact]
    public void GetPageSize_CustomDefault()
    {
        var args = new LoadDataArgs();
        Assert.Equal(10, args.GetPageSize(10));
    }

    [Fact]
    public void ToPageRequest_Combined()
    {
        var args = new LoadDataArgs { Skip = 20, Top = 10 };
        var (page, pageSize) = args.ToPageRequest();
        Assert.Equal(3, page);
        Assert.Equal(10, pageSize);
    }

    [Fact]
    public void ToPageRequest_Defaults()
    {
        var args = new LoadDataArgs();
        var (page, pageSize) = args.ToPageRequest();
        Assert.Equal(1, page);
        Assert.Equal(LoadDataArgsExtensions.DefaultPageSize, pageSize);
    }

    [Fact]
    public void DefaultPageSize_Is25()
    {
        Assert.Equal(25, LoadDataArgsExtensions.DefaultPageSize);
    }
}
