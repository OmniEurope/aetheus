// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests;

public class PipelineConfigTemplateRendererTests
{
    [Theory]
    [InlineData(".pipeline/configs/apache/site.conf", true)]
    [InlineData(".pipeline\\configs\\apache\\site.conf", true)]
    [InlineData(".pipeline/site.conf", false)]
    [InlineData(".pipeline/configs/../secret.conf", false)]
    [InlineData("/etc/apache2/site.conf", false)]
    public void IsSafeConfigPath_EnforcesVersionedConfigDirectory(string path, bool expected)
        => Assert.Equal(expected, PipelineConfigTemplateRenderer.IsSafeConfigPath(path));

    [Fact]
    public void RenderStrict_ReplacesOnlyDeclaredTokens()
    {
        var result = PipelineConfigTemplateRenderer.RenderStrict(
            "ServerName #{HOST}#\nProxyPass / http://127.0.0.1:#{PORT}#/\n",
            new Dictionary<string, string> { ["HOST"] = "app.example.test", ["PORT"] = "10031" });

        Assert.Equal(
            "ServerName app.example.test\nProxyPass / http://127.0.0.1:10031/\n",
            result);
    }

    [Theory]
    [InlineData("ServerName #{MISSING}#", "Configuration template variable 'MISSING' is not defined")]
    [InlineData("ServerName #{MALFORMED}", "invalid or unresolved token")]
    public void RenderStrict_UnresolvedOrMalformedToken_IsRejected(string template, string expectedMessage)
    {
        var error = Assert.Throws<BadRequestException>(() =>
            PipelineConfigTemplateRenderer.RenderStrict(template, new Dictionary<string, string>()));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("line1\nline2")]
    [InlineData("line1\rline2")]
    [InlineData("value\0tail")]
    public void RenderStrict_LineBreakingReplacement_IsRejected(string value)
    {
        var error = Assert.Throws<BadRequestException>(() =>
            PipelineConfigTemplateRenderer.RenderStrict(
                "ServerName #{HOST}#", new Dictionary<string, string> { ["HOST"] = value }));

        Assert.Contains("forbidden line break or NUL", error.Message, StringComparison.Ordinal);
    }
}
