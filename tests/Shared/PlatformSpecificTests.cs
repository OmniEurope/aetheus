// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.CompilerServices;
using Xunit.Sdk;
using Xunit.v3;

namespace Aetheus.Tests.Shared;

/// <summary>
/// A test that only makes sense on one operating system ("windows" or "linux"). On any other, it is
/// not discovered at all: it neither runs nor counts as skipped, so every suite reports zero skipped
/// on this host and on the Linux CI runner alike, and a plain <c>dotnet test</c> behaves like the
/// launchers.
/// <para>
/// This used to be a default <c>Platform!=...</c> filter on every run. Microsoft.Testing.Platform
/// cannot carry one: xunit refuses to combine its trait filter with the <c>--filter</c> any caller
/// adds, so the platform is decided at discovery instead, where no command line can undo it.
/// </para>
/// </summary>
public interface IPlatformSpecificTest
{
    /// <summary>"windows" or "linux".</summary>
    string Platform { get; }
}

[XunitTestCaseDiscoverer(typeof(PlatformFactDiscoverer))]
public sealed class PlatformFactAttribute(
    string platform,
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1)
    : FactAttribute(sourceFilePath, sourceLineNumber), IPlatformSpecificTest
{
    public string Platform { get; } = platform;
}

[XunitTestCaseDiscoverer(typeof(PlatformTheoryDiscoverer))]
public sealed class PlatformTheoryAttribute(
    string platform,
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1)
    : TheoryAttribute(sourceFilePath, sourceLineNumber), IPlatformSpecificTest
{
    public string Platform { get; } = platform;
}

public sealed class PlatformFactDiscoverer : FactDiscoverer
{
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, IFactAttribute factAttribute) =>
        TestPlatform.IsCurrent(factAttribute)
            ? base.Discover(discoveryOptions, testMethod, factAttribute)
            : new([]);
}

public sealed class PlatformTheoryDiscoverer : TheoryDiscoverer
{
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, IFactAttribute factAttribute) =>
        TestPlatform.IsCurrent(factAttribute)
            ? base.Discover(discoveryOptions, testMethod, factAttribute)
            : new([]);
}

internal static class TestPlatform
{
    public static bool IsCurrent(IFactAttribute attribute) =>
        ((IPlatformSpecificTest)attribute).Platform switch
        {
            "windows" => OperatingSystem.IsWindows(),
            "linux" => OperatingSystem.IsLinux(),
            var other => throw new ArgumentException($"Unknown test platform '{other}'.", nameof(attribute))
        };
}
