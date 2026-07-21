// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Docker;

namespace Aetheus.Back.Tests;

public class DockerIdValidatorTests
{
    // --- Container IDs ---

    [Theory]
    [InlineData("abc123def456", true)]
    [InlineData("abc123def456abc123def456abc123def456abc123def456abc123def456abcd", true)]
    [InlineData("AABBCCDDEE12", true)]
    [InlineData("abc123def45", false)]       // 11 chars - too short
    [InlineData("", false)]
    [InlineData("abc123def45g", false)]       // 'g' not hex
    [InlineData("abc123; rm -rf /", false)]   // injection attempt
    [InlineData("abc 123def456", false)]      // space
    public void IsValidContainerId(string id, bool expected)
    {
        Assert.Equal(expected, DockerIdValidator.IsValidContainerId(id));
    }

    [Fact]
    public void IsValidContainerId_Null_ReturnsFalse()
    {
        Assert.False(DockerIdValidator.IsValidContainerId(null!));
    }

    // --- Image IDs ---

    [Theory]
    [InlineData("abc123def456", true)]
    [InlineData("sha256:abc123def456abc123def456abc123def456abc123def456abc123def456abcd", true)]
    [InlineData("SHA256:AABB", false)]
    [InlineData("abc123; rm -rf /", false)]
    [InlineData("", false)]
    public void IsValidImageId(string id, bool expected)
    {
        Assert.Equal(expected, DockerIdValidator.IsValidImageId(id));
    }

    // --- Image names ---

    [Theory]
    [InlineData("nginx", true)]
    [InlineData("nginx:latest", true)]
    [InlineData("docker.io/library/nginx:1.25", true)]
    [InlineData("my-registry.com:5000/app/web:v2.1", true)]
    [InlineData("", false)]
    [InlineData("; rm -rf /", false)]
    [InlineData(".invalid", false)]           // starts with dot
    public void IsValidImageName(string name, bool expected)
    {
        Assert.Equal(expected, DockerIdValidator.IsValidImageName(name));
    }

    // --- Compose / Network / Volume names ---

    [Theory]
    [InlineData("my-stack", true)]
    [InlineData("stack_v2", true)]
    [InlineData("prod.monitoring", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("-invalid", false)]           // starts with dash
    [InlineData("stack; echo pwned", false)]  // injection
    public void IsValidName(string name, bool expected)
    {
        Assert.Equal(expected, DockerIdValidator.IsValidName(name));
    }
}
