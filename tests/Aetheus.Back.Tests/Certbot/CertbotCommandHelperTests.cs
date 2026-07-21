// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Certbot;

namespace Aetheus.Back.Tests.Certbot;

public class CertbotCommandHelperTests
{
    // --- IsValidCertName ---

    [Theory]
    [InlineData("my-cert", true)]
    [InlineData("cert.example.com", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("-invalid", false)]
    public void IsValidCertName(string name, bool expected)
    {
        Assert.Equal(expected, CertbotCommandHelper.IsValidCertName(name));
    }

    // --- IsValidDomainList ---

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("example.com,sub.example.com", true)]
    [InlineData("*.example.com", true)]
    [InlineData("", false)]
    [InlineData("not valid!", false)]
    public void IsValidDomainList(string domains, bool expected)
    {
        Assert.Equal(expected, CertbotCommandHelper.IsValidDomainList(domains));
    }

    // --- IsValidEmail ---

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("", false)]
    [InlineData("not-an-email", false)]
    public void IsValidEmail(string email, bool expected)
    {
        Assert.Equal(expected, CertbotCommandHelper.IsValidEmail(email));
    }

    // --- IsValidWebrootPath ---

    [Theory]
    [InlineData("/var/www/html", true)]
    [InlineData("/tmp", true)]
    [InlineData("", false)]
    [InlineData("relative/path", false)]
    public void IsValidWebrootPath(string path, bool expected)
    {
        Assert.Equal(expected, CertbotCommandHelper.IsValidWebrootPath(path));
    }
}
