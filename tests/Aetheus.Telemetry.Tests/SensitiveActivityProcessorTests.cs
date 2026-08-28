// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Telemetry.Tests;

public sealed class SensitiveActivityProcessorTests
{
    [Fact]
    public void OnEnd_RemovesForbiddenTraceAttributesAndPreservesSafeDimensions()
    {
        using var activity = new Activity("privacy-test").Start();
        activity.SetTag("url.query", "?token=secret");
        activity.SetTag("url.full", "https://example.invalid/private?token=secret");
        activity.SetTag("http.url", "https://example.invalid/legacy?token=secret");
        activity.SetTag("http.target", "/legacy?token=secret");
        activity.SetTag("url.fragment", "private");
        activity.SetTag("http.request.header.authorization", "Bearer secret");
        activity.SetTag("http.request.header.cookie", "session=secret");
        activity.SetTag("http.response.header.set_cookie", "session=secret");
        activity.SetTag("http.request.body", "email=person@example.invalid");
        activity.SetTag("db.statement", "select * from users");
        activity.SetTag("db.query.text", "select password from users");
        activity.SetTag("db.query.parameter.0", "secret");
        activity.SetTag("user.email", "person@example.invalid");
        activity.SetTag("http.route", "/orders/{id}");

        new SensitiveActivityProcessor().OnEnd(activity);

        Assert.Null(activity.GetTagItem("url.query"));
        Assert.Null(activity.GetTagItem("url.full"));
        Assert.Null(activity.GetTagItem("http.url"));
        Assert.Null(activity.GetTagItem("http.target"));
        Assert.Null(activity.GetTagItem("url.fragment"));
        Assert.Null(activity.GetTagItem("http.request.header.authorization"));
        Assert.Null(activity.GetTagItem("http.request.header.cookie"));
        Assert.Null(activity.GetTagItem("http.response.header.set_cookie"));
        Assert.Null(activity.GetTagItem("http.request.body"));
        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Null(activity.GetTagItem("db.query.text"));
        Assert.Null(activity.GetTagItem("db.query.parameter.0"));
        Assert.Null(activity.GetTagItem("user.email"));
        Assert.Equal("/orders/{id}", activity.GetTagItem("http.route"));
    }
}
