// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests;

public class TestResultParserTests
{
    // --- JUnit format ---

    [Fact]
    public void Parse_JUnit_AllPassed_ReturnsCorrectResults()
    {
        var xml = """
            <testsuite name="MySuite" tests="2">
                <testcase name="Test1" classname="MyTests" time="0.5" />
                <testcase name="Test2" classname="MyTests" time="1.2" />
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, "build", "test-step");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(TestOutcome.Passed, r.Outcome));
        Assert.All(results, r => Assert.Equal(1, r.PipelineRunId));
        Assert.All(results, r => Assert.Equal("build", r.StageName));
        Assert.All(results, r => Assert.Equal("test-step", r.StepName));
        Assert.Equal("Test1", results[0].TestName);
        Assert.Equal("MyTests", results[0].TestSuite);
        Assert.Equal(500, results[0].DurationMs);
        Assert.Equal(1200, results[1].DurationMs);
    }

    [Fact]
    public void Parse_JUnit_WithFailure_ReturnsFailedOutcome()
    {
        var xml = """
            <testsuite name="MySuite" tests="1">
                <testcase name="FailTest" classname="MyTests" time="0.1">
                    <failure message="Expected 1 but got 2">Stack trace here</failure>
                </testcase>
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Single(results);
        Assert.Equal(TestOutcome.Failed, results[0].Outcome);
        Assert.Equal("Expected 1 but got 2", results[0].ErrorMessage);
        Assert.Equal("Stack trace here", results[0].StackTrace);
    }

    [Fact]
    public void Parse_JUnit_WithError_ReturnsErrorOutcome()
    {
        var xml = """
            <testsuite name="Suite" tests="1">
                <testcase name="ErrorTest" classname="Tests" time="0.3">
                    <error message="NullRef">at line 42</error>
                </testcase>
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 2, "stage1", "step1");

        Assert.Single(results);
        Assert.Equal(TestOutcome.Error, results[0].Outcome);
        Assert.Equal("NullRef", results[0].ErrorMessage);
        Assert.Equal("at line 42", results[0].StackTrace);
    }

    [Fact]
    public void Parse_JUnit_WithSkipped_ReturnsSkippedOutcome()
    {
        var xml = """
            <testsuite name="Suite" tests="1">
                <testcase name="SkipTest" classname="Tests" time="0">
                    <skipped />
                </testcase>
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Single(results);
        Assert.Equal(TestOutcome.Skipped, results[0].Outcome);
    }

    [Fact]
    public void Parse_JUnit_MixedResults_CountsCorrectly()
    {
        var xml = """
            <testsuites>
                <testsuite name="Suite1" tests="4">
                    <testcase name="Pass1" classname="T" time="0.1" />
                    <testcase name="Fail1" classname="T" time="0.2">
                        <failure message="boom" />
                    </testcase>
                    <testcase name="Skip1" classname="T" time="0">
                        <skipped />
                    </testcase>
                    <testcase name="Error1" classname="T" time="0.3">
                        <error message="crash" />
                    </testcase>
                </testsuite>
            </testsuites>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Equal(4, results.Count);
        Assert.Equal(1, results.Count(r => r.Outcome == TestOutcome.Passed));
        Assert.Equal(1, results.Count(r => r.Outcome == TestOutcome.Failed));
        Assert.Equal(1, results.Count(r => r.Outcome == TestOutcome.Skipped));
        Assert.Equal(1, results.Count(r => r.Outcome == TestOutcome.Error));
    }

    [Fact]
    public void Parse_JUnit_NoClassname_FallsBackToSuiteName()
    {
        var xml = """
            <testsuite name="FallbackSuite" tests="1">
                <testcase name="Test" time="0.1" />
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Single(results);
        Assert.Equal("FallbackSuite", results[0].TestSuite);
    }

    // --- xUnit format ---

    [Fact]
    public void Parse_XUnit_PassingTests_ReturnsCorrectResults()
    {
        var xml = """
            <assemblies>
                <assembly name="Tests.dll">
                    <collection name="Tests">
                        <test name="MyTest.ShouldPass" type="MyTest" result="Pass" time="0.05" />
                        <test name="MyTest.ShouldAlsoPass" type="MyTest" result="Pass" time="0.10" />
                    </collection>
                </assembly>
            </assemblies>
            """;

        var results = TestResultParser.Parse(xml, "xunit", 3, "stage", "step");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(TestOutcome.Passed, r.Outcome));
        Assert.Equal("MyTest", results[0].TestSuite);
        Assert.Equal(50, results[0].DurationMs);
    }

    [Fact]
    public void Parse_XUnit_FailedTest_ReturnsFailureInfo()
    {
        var xml = """
            <assemblies>
                <assembly name="Tests.dll">
                    <collection name="Tests">
                        <test name="FailTest" type="Suite" result="Fail" time="0.2">
                            <failure>
                                <message>Assert.Equal failed</message>
                                <stack-trace>at MyTest.cs:line 10</stack-trace>
                            </failure>
                        </test>
                    </collection>
                </assembly>
            </assemblies>
            """;

        var results = TestResultParser.Parse(xml, "xunit", 1, null, null);

        Assert.Single(results);
        Assert.Equal(TestOutcome.Failed, results[0].Outcome);
        Assert.Equal("Assert.Equal failed", results[0].ErrorMessage);
        Assert.Equal("at MyTest.cs:line 10", results[0].StackTrace);
    }

    [Fact]
    public void Parse_XUnit_SkippedTest_ReturnsSkipped()
    {
        var xml = """
            <assemblies>
                <assembly name="Tests.dll">
                    <collection name="Tests">
                        <test name="SkipTest" type="Suite" result="Skip" time="0" />
                    </collection>
                </assembly>
            </assemblies>
            """;

        var results = TestResultParser.Parse(xml, "xunit", 1, null, null);

        Assert.Single(results);
        Assert.Equal(TestOutcome.Skipped, results[0].Outcome);
    }

    [Fact]
    public void Parse_Trx_RealFixture_MapsPassedFailedAndNotExecuted()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <TestDefinitions>
                <UnitTest name="Passes" id="11111111-1111-1111-1111-111111111111">
                  <TestMethod className="Sample.Tests" name="Passes" />
                </UnitTest>
                <UnitTest name="Fails" id="22222222-2222-2222-2222-222222222222">
                  <TestMethod className="Sample.Tests" name="Fails" />
                </UnitTest>
                <UnitTest name="Skipped" id="33333333-3333-3333-3333-333333333333">
                  <TestMethod className="Sample.OtherTests" name="Skipped" />
                </UnitTest>
              </TestDefinitions>
              <Results>
                <UnitTestResult testId="11111111-1111-1111-1111-111111111111" testName="Passes" outcome="Passed" duration="00:00:00.3000000" />
                <UnitTestResult testId="22222222-2222-2222-2222-222222222222" testName="Fails" outcome="Failed" duration="00:00:01.2500000">
                  <Output><ErrorInfo><Message>Expected true</Message><StackTrace>at Sample.Tests.Fails()</StackTrace></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testId="33333333-3333-3333-3333-333333333333" testName="Skipped" outcome="NotExecuted" />
              </Results>
            </TestRun>
            """;

        var results = TestResultParser.Parse(xml, "trx", 1, "s", "t");

        Assert.Equal(3, results.Count);
        Assert.Equal(TestOutcome.Passed, results[0].Outcome);
        Assert.Equal(300, results[0].DurationMs);
        Assert.Equal("Sample.Tests", results[0].TestSuite);
        Assert.Equal(TestOutcome.Failed, results[1].Outcome);
        Assert.Equal(1250, results[1].DurationMs);
        Assert.Equal("Expected true", results[1].ErrorMessage);
        Assert.Equal("at Sample.Tests.Fails()", results[1].StackTrace);
        Assert.Equal(TestOutcome.Skipped, results[2].Outcome);
        Assert.Equal("Sample.OtherTests", results[2].TestSuite);
    }

    // --- Edge cases ---

    [Fact]
    public void Parse_InvalidXml_ReturnsEmpty()
    {
        var results = TestResultParser.Parse("<invalid>xml", "junit", 1, null, null);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_EmptyTestSuite_ReturnsEmpty()
    {
        var xml = """
            <testsuite name="Empty" tests="0">
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_UnknownFormat_ReturnsEmpty()
    {
        var xml = "<root><item /></root>";

        var results = TestResultParser.Parse(xml, "nunit", 1, null, null);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_MissingTimeAttribute_DefaultsToZero()
    {
        var xml = """
            <testsuite name="Suite" tests="1">
                <testcase name="NoTime" classname="Tests" />
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Single(results);
        Assert.Equal(0, results[0].DurationMs);
    }

    [Fact]
    public void Parse_MissingName_DefaultsToUnknown()
    {
        var xml = """
            <testsuite name="Suite" tests="1">
                <testcase classname="Tests" time="0.1" />
            </testsuite>
            """;

        var results = TestResultParser.Parse(xml, "junit", 1, null, null);

        Assert.Single(results);
        Assert.Equal("Unknown", results[0].TestName);
    }
}
