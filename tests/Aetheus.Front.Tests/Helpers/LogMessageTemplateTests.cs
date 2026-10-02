// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Helpers;

/// <summary>
/// The production log viewer printed "Received HTTP response headers after {ElapsedMilliseconds}ms -
/// {StatusCode}" (2026-09-12): the OTLP body is the message template and the values travel as
/// attributes. These pin the substitution the viewer now does.
/// </summary>
public class LogMessageTemplateTests
{
    private const string Attributes =
        "{\"ElapsedMilliseconds\":\"14.64\",\"StatusCode\":\"200\",\"HttpMethod\":\"GET\",\"Uri\":\"https://www.sonytumen.com/\"}";

    [Fact]
    public void Render_ReplacesEveryHoleWithItsAttribute()
    {
        var rendered = LogMessageTemplate.Render(
            "Received HTTP response headers after {ElapsedMilliseconds}ms - {StatusCode}", Attributes);

        Assert.Equal("Received HTTP response headers after 14.64ms - 200", rendered);
    }

    [Fact]
    public void Render_ReadsNamesCarryingAFormatOrAnAlignment()
    {
        var rendered = LogMessageTemplate.Render("{HttpMethod,-6} {Uri:l} took {ElapsedMilliseconds:0.00}", Attributes);

        Assert.Equal("GET https://www.sonytumen.com/ took 14.64", rendered);
    }

    [Fact]
    public void Render_KeepsAHoleWithoutAttributeAndEscapedBraces()
    {
        var rendered = LogMessageTemplate.Render("{{literal}} {StatusCode} for {Missing}", Attributes);

        Assert.Equal("{literal} 200 for {Missing}", rendered);
    }

    [Fact]
    public void Render_NonStringAttributesUseTheirJsonText()
    {
        var rendered = LogMessageTemplate.Render("Retry {Attempt} ok={Ok}", "{\"Attempt\":3,\"Ok\":true}");

        Assert.Equal("Retry 3 ok=true", rendered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void Render_WithoutReadableAttributes_ReturnsTheTemplate(string? attributes)
    {
        Assert.Equal("HTTP {StatusCode}", LogMessageTemplate.Render("HTTP {StatusCode}", attributes));
    }

    [Fact]
    public void Render_UnclosedHole_IsKeptVerbatim()
    {
        Assert.Equal("value {StatusCode", LogMessageTemplate.Render("value {StatusCode", Attributes));
    }
}
