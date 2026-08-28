// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.TestManagement;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class TestManagementServiceTests
{
    private readonly ITestManagementRepository _repo = Substitute.For<ITestManagementRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly TestManagementService _sut;

    public TestManagementServiceTests()
    {
        _sut = new TestManagementService(_repo, _audit, TimeProvider.System);
    }

    [Fact]
    public async Task GetSuitesAsync_ReturnsMappedList()
    {
        _repo.GetSuitesAsync(null, Arg.Any<List<int>?>(), Arg.Any<CancellationToken>())
            .Returns([new TestSuite
            {
                Id = 1, Name = "Unit Tests", ProjectId = 1,
                TestCases = [
                    new TestCase { LastOutcome = TestOutcome.Passed },
                    new TestCase { LastOutcome = TestOutcome.Failed }
                ]
            }]);

        var result = await _sut.GetSuitesAsync(projectId: null, accessibleProjectIds: null, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Unit Tests", result[0].Name);
        Assert.Equal(2, result[0].TestCaseCount);
    }

    [Fact]
    public async Task GetSuiteDetailAsync_Found_ReturnsDtoWithTestCases()
    {
        _repo.GetSuiteDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TestSuite
            {
                Id = 1,
                Name = "Integration",
                ProjectId = 1,
                TestCases = [new TestCase { Id = 1, Name = "MyTest", LastOutcome = TestOutcome.Passed }]
            });

        var result = await _sut.GetSuiteDetailAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.TestCases);
        Assert.Equal("MyTest", result.TestCases[0].Name);
    }

    [Fact]
    public async Task GetSuiteDetailAsync_NotFound_ReturnsNull()
    {
        _repo.GetSuiteDetailAsync(99, Arg.Any<CancellationToken>())
            .Returns((TestSuite?)null);

        Assert.Null(await _sut.GetSuiteDetailAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateSuiteAsync_CreatesAndReturnsDto()
    {
        _repo.AddSuiteAsync(Arg.Any<TestSuite>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateSuiteAsync(new CreateTestSuiteRequest
        {
            ProjectId = 1,
            Name = "E2E Suite",
            Type = TestSuiteType.Automated
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("E2E Suite", result.Name);
        await _repo.Received(1).AddSuiteAsync(Arg.Any<TestSuite>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Created", "TestSuite", Arg.Any<int>(), "E2E Suite", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateSuiteAsync_NotFound_ReturnsNull()
    {
        _repo.FindSuiteAsync(99, Arg.Any<CancellationToken>())
            .Returns((TestSuite?)null);

        Assert.Null(await _sut.UpdateSuiteAsync(99, new UpdateTestSuiteRequest { Name = "x" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateSuiteAsync_Found_UpdatesAndReturns()
    {
        _repo.FindSuiteAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TestSuite { Id = 1, Name = "old", ProjectId = 1, TestCases = [] });
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.UpdateSuiteAsync(1, new UpdateTestSuiteRequest { Name = "new" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("new", result.Name);
        await _audit.Received(1).LogAsync("Updated", "TestSuite", 1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteSuiteAsync_NotFound_ReturnsFalse()
    {
        _repo.FindSuiteAsync(99, Arg.Any<CancellationToken>())
            .Returns((TestSuite?)null);

        Assert.False(await _sut.DeleteSuiteAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSuiteAsync_Found_DeletesAndReturnsTrue()
    {
        _repo.FindSuiteAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TestSuite { Id = 1, Name = "suite" });
        _repo.RemoveSuiteAsync(Arg.Any<TestSuite>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Assert.True(await _sut.DeleteSuiteAsync(1, ct: TestContext.Current.CancellationToken));
        await _audit.Received(1).LogAsync("Deleted", "TestSuite", 1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestTestResultsAsync_ValidJUnit_ReturnsIngestionResult()
    {
        var xml = """
            <testsuite tests="2">
                <testcase classname="MyTests" name="Test1" time="0.5" />
                <testcase classname="MyTests" name="Test2" time="0.3">
                    <failure message="fail" />
                </testcase>
            </testsuite>
            """;

        _repo.FindSuiteByNameAsync(1, "Results", Arg.Any<CancellationToken>())
            .Returns((TestSuite?)null);
        _repo.AddSuiteAsync(Arg.Any<TestSuite>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.FindTestCaseByMethodAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TestCase?)null);
        _repo.AddTestCaseAsync(Arg.Any<TestCase>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.IngestTestResultsAsync(new IngestTestResultsRequest
        {
            ProjectId = 1,
            SuiteName = "Results",
            Format = "junit",
            XmlContent = xml
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCases);
        Assert.Equal(2, result.NewCases);
    }

    [Fact]
    public async Task IngestTestResultsAsync_XUnitFormat_ParsesCorrectly()
    {
        var xml = """
            <assemblies>
                <assembly>
                    <collection>
                        <test name="Namespace.MyTests.PassTest" result="Pass" time="0.1" />
                        <test name="Namespace.MyTests.FailTest" result="Fail" time="0.2">
                            <failure><message>fail</message></failure>
                        </test>
                        <test name="Namespace.MyTests.SkipTest" result="Skip" time="0" />
                    </collection>
                </assembly>
            </assemblies>
            """;

        _repo.FindSuiteByNameAsync(1, "XResults", Arg.Any<CancellationToken>())
            .Returns((TestSuite?)null);
        _repo.AddSuiteAsync(Arg.Any<TestSuite>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.IngestTestResultsAsync(new IngestTestResultsRequest
        {
            ProjectId = 1,
            SuiteName = "XResults",
            Format = "xunit",
            XmlContent = xml
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCases);
        Assert.Equal(3, result.NewCases);
    }

    [Fact]
    public async Task IngestTestResultsAsync_ExistingSuite_UpdatesExistingCases()
    {
        var existingSuite = new TestSuite
        {
            Id = 5,
            ProjectId = 1,
            Name = "Results",
            TestCases = [
                new TestCase
                {
                    Id = 10,
                    AutomatedTestClass = "MyTests",
                    AutomatedTestMethod = "Test1",
                    LastOutcome = TestOutcome.Passed
                }
            ]
        };
        var xml = """
            <testsuite tests="1">
                <testcase classname="MyTests" name="Test1" time="0.9">
                    <failure message="now fails" />
                </testcase>
            </testsuite>
            """;

        _repo.FindSuiteByNameAsync(1, "Results", Arg.Any<CancellationToken>())
            .Returns(existingSuite);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.IngestTestResultsAsync(new IngestTestResultsRequest
        {
            ProjectId = 1,
            SuiteName = "Results",
            Format = "junit",
            XmlContent = xml
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TotalCases);
        Assert.Equal(0, result.NewCases);
        Assert.Equal(1, result.UpdatedCases);
    }

    [Fact]
    public async Task IngestTestResultsAsync_JUnit_ErrorAndSkipped_ParsedCorrectly()
    {
        var xml = """
            <testsuite tests="2">
                <testcase classname="Tests" name="ErrTest" time="0.1">
                    <error message="err" />
                </testcase>
                <testcase classname="Tests" name="SkipTest">
                    <skipped />
                </testcase>
            </testsuite>
            """;

        _repo.FindSuiteByNameAsync(1, "Mix", Arg.Any<CancellationToken>())
            .Returns((TestSuite?)null);
        _repo.AddSuiteAsync(Arg.Any<TestSuite>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.IngestTestResultsAsync(new IngestTestResultsRequest
        {
            ProjectId = 1,
            SuiteName = "Mix",
            Format = "junit",
            XmlContent = xml
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCases);
    }
}

