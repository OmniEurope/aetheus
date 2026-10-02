// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.SystemLogs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests.SystemLogs;

/// <summary>
/// F-004. Every field of a client error report is caller-controlled, so a newline in any of them
/// forged whole entries in a text log sink: a report could fabricate an unrelated event, or push a
/// real one out of view. Lengths were already bounded on the DTO; what was missing is that a value
/// must stay ONE line.
/// </summary>
public sealed class ClientErrorsControllerTests
{
    private readonly ILogger<ClientErrorsController> _logger = Substitute.For<ILogger<ClientErrorsController>>();

    private ClientErrorsController Sut() => new(_logger);

    [Fact]
    public void Report_NewlineInAnyField_IsFlattenedBeforeLogging()
    {
        var captured = new List<object?>();
        _logger.When(logger => logger.Log(
                Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(),
                Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>()))
            .Do(call => captured.AddRange(
                ((IReadOnlyList<KeyValuePair<string, object?>>)call[2]!).Select(pair => pair.Value)));

        var result = Sut().Report(new ClientErrorLogRequest
        {
            CorrelationId = "abc\r\ndef",
            Summary = "boom\nFATAL forged entry",
            Message = "line one\nline two",
            Path = "/pipelines\n/admin"
        });

        Assert.IsType<NoContentResult>(result);
        var strings = captured.OfType<string>().ToList();
        Assert.NotEmpty(strings);
        Assert.All(strings, value =>
        {
            Assert.DoesNotContain('\n', value);
            Assert.DoesNotContain('\r', value);
        });
        // Flattened, not truncated: the report is still readable, it just cannot forge a line.
        Assert.Contains(strings, value => value.Contains("FATAL forged entry", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_MissingPath_LogsTheUnknownPlaceholderRatherThanNull()
    {
        string? logged = null;
        _logger.When(logger => logger.Log(
                Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(),
                Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>()))
            .Do(call => logged = ((IReadOnlyList<KeyValuePair<string, object?>>)call[2]!)
                .First(pair => pair.Key == "ClientPath").Value as string);

        Sut().Report(new ClientErrorLogRequest
        {
            CorrelationId = "abc",
            Summary = "boom",
            Message = "details",
            Path = null
        });

        Assert.Equal("(unknown)", logged);
    }
}
