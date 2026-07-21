// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Back.Components.Shared;

namespace Aetheus.Back.Tests.Webhooks;

public class WebhookSsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    public void Loopback_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    public void PrivateClassA_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    public void PrivateClassB_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void PrivateClassB_BelowRange_IsAllowed()
    {
        Assert.False(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("172.15.0.1")));
    }

    [Theory]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.1")]
    public void PrivateClassC_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]
    public void LinkLocal_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    public void NonPublicInfrastructureRanges_AreForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    public void ZeroNetwork_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("255.255.255.255")]
    public void Multicast_IsForbidden(string ip)
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("203.0.113.1")]
    public void PublicIPv4_IsAllowed(string ip)
    {
        Assert.False(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IPv6Loopback_IsForbidden()
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void IPv6Any_IsForbidden()
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.IPv6Any));
    }

    [Fact]
    public void IPv6LinkLocal_IsForbidden()
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("fe80::1")));
    }

    [Fact]
    public void IPv6UniqueLocal_IsForbidden()
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("fc00::1")));
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("fd00::1")));
    }

    [Fact]
    public void IPv4MappedToIPv6_PrivateRange_IsForbidden()
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("::ffff:10.0.0.1")));
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("::ffff:192.168.1.1")));
    }

    [Fact]
    public void IPv4MappedToIPv6_PublicRange_IsAllowed()
    {
        Assert.False(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("::ffff:8.8.8.8")));
    }

    [Fact]
    public void IPv6PublicAddress_IsAllowed()
    {
        Assert.False(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void IPv6Multicast_IsForbidden()
    {
        Assert.True(WebhookSsrfGuard.IsForbiddenAddress(IPAddress.Parse("ff02::1")));
    }
}
