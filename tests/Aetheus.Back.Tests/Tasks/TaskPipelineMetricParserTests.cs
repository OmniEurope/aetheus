// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks;

namespace Aetheus.Back.Tests.Tasks;

public sealed class TaskPipelineMetricParserTests
{
    [Fact]
    public void Parse_ProducesBoundedTypedRunMetrics()
    {
        var createdAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var output = "noise\n##aetheus[pipelinemetric key=artifact.collect.compress;type=Duration;unit=s]1.25\n"
                     + "##aetheus[pipelinemetric key=artifact.bytes.source;type=Size;unit=bytes]4096";

        var metrics = TaskPipelineMetricParser.Parse(output, 42, "Package", "Collect", createdAt);

        Assert.Collection(metrics,
            duration =>
            {
                Assert.Equal("artifact.collect.compress", duration.Key);
                Assert.Equal(RunMetricType.Duration, duration.Type);
                Assert.Equal(1.25, duration.Value);
                Assert.Equal("s", duration.Unit);
            },
            size =>
            {
                Assert.Equal("artifact.bytes.source", size.Key);
                Assert.Equal(RunMetricType.Size, size.Type);
                Assert.Equal(4096, size.Value);
                Assert.Equal("bytes", size.Unit);
            });
        Assert.All(metrics, metric =>
        {
            Assert.Equal(42, metric.PipelineRunId);
            Assert.Equal("Package", metric.StageName);
            Assert.Equal("Collect", metric.StepName);
            Assert.Equal(createdAt, metric.CreatedAt);
        });
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("secret value")]
    public void Parse_RejectsNonFiniteOrNonNumericValues(string value)
    {
        var output = $"##aetheus[pipelinemetric key=artifact.bad;type=Score;unit=x]{value}";

        Assert.Empty(TaskPipelineMetricParser.Parse(output, 1, null, null, DateTime.UnixEpoch));
    }
}
