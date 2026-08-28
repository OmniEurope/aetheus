// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// The scanner argument resolver builds the command line handed to a security scanner. Two properties
/// matter beyond substitution: a Gitleaks history range must be two immutable full SHAs, because a
/// mutable ref (a branch name, HEAD~5) lets the scanned range change under the report that claims to
/// cover it; and every placeholder must be replaced, since an unreplaced one reaches the scanner as a
/// literal path that does not exist.
/// </summary>
public class ScannerOperationValidatorTests
{
    // Called directly: InternalsVisibleTo already exposes the internal type, so a rename breaks the
    // build instead of failing at runtime.
    private static List<string> ResolveArguments(
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? envVars = null,
        string source = "/src",
        string report = "/report.json",
        string rules = "/rules.toml",
        string output = "/out") =>
        ScannerOperationValidator.ResolveArguments(
            arguments, envVars ?? new Dictionary<string, string>(StringComparer.Ordinal),
            source, report, rules, output);

    private static Dictionary<string, string> Env(params (string Key, string Value)[] entries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries) result[key] = value;
        return result;
    }

    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string OtherSha = "fedcba9876543210fedcba9876543210fedcba98";

    // --- placeholder substitution -------------------------------------------

    [Fact]
    public void ThePathPlaceholdersAreAllSubstituted()
    {
        var resolved = ResolveArguments(["{source}", "{report}", "{rules}", "{output}"]);

        Assert.Equal(["/src", "/report.json", "/rules.toml", "/out"], resolved);
    }

    [Fact]
    public void SeveralPlaceholdersInOneArgumentAreAllReplaced()
    {
        var resolved = ResolveArguments(["--src={source}:--out={output}"]);

        Assert.Equal("--src=/src:--out=/out", Assert.Single(resolved));
    }

    [Fact]
    public void AnArgumentWithNoPlaceholderIsPassedThroughUnchanged()
    {
        var resolved = ResolveArguments(["--severity=high", "--quiet"]);

        Assert.Equal(["--severity=high", "--quiet"], resolved);
    }

    [Fact]
    public void TheTargetUrlComesFromTheEnvironment()
    {
        var resolved = ResolveArguments(
            ["{targetUrl}"], Env(("AETHEUS_SCANNER_TARGET_URL", "https://staging.example.com")));

        Assert.Equal("https://staging.example.com", Assert.Single(resolved));
    }

    [Fact]
    public void TheApiSpecificationUrlAndFormatComeFromTheEnvironment()
    {
        var resolved = ResolveArguments(
            ["{apiSpecUrl}", "{apiFormat}"],
            Env(("AETHEUS_SCANNER_API_SPECIFICATION_URL", "https://x/openapi.json"),
                ("AETHEUS_SCANNER_API_SPECIFICATION_FORMAT", "openapi")));

        Assert.Equal(["https://x/openapi.json", "openapi"], resolved);
    }

    [Fact]
    public void AMissingEnvironmentValueBecomesEmpty_NotTheLiteralPlaceholder()
    {
        // A literal "{targetUrl}" would reach the scanner as a hostname it then tries to resolve.
        var resolved = ResolveArguments(["{targetUrl}"]);

        Assert.Equal(string.Empty, Assert.Single(resolved));
    }

    [Fact]
    public void AnEmptyArgumentListResolvesToNothing()
    {
        Assert.Empty(ResolveArguments([]));
    }

    // --- gitleaks history range: immutability ------------------------------

    [Fact]
    public void AFullShaRangeIsAccepted()
    {
        var resolved = ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", $"{Sha}..{OtherSha}")));

        Assert.Equal($"{Sha}..{OtherSha}", Assert.Single(resolved));
    }

    [Fact]
    public void ASha256RangeIsAlsoAccepted()
    {
        var sha256 = new string('a', 64);
        var other = new string('b', 64);

        var resolved = ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", $"{sha256}..{other}")));

        Assert.Equal($"{sha256}..{other}", Assert.Single(resolved));
    }

    [Fact]
    public void ABranchNameRangeIsRefused()
    {
        // "main..develop" means something different tomorrow; the report would claim to cover a range
        // that has since moved.
        Assert.Throws<IOException>(() => ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", "main..develop"))));
    }

    [Fact]
    public void ARelativeRevisionRangeIsRefused()
    {
        Assert.Throws<IOException>(() => ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", $"HEAD~5..{Sha}"))));
    }

    [Fact]
    public void AnAbbreviatedShaRangeIsRefused()
    {
        // A short SHA is not guaranteed unique as the repository grows.
        Assert.Throws<IOException>(() => ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", "0123456..fedcba9"))));
    }

    [Fact]
    public void ASingleRevisionWithoutARangeIsRefused()
    {
        Assert.Throws<IOException>(() => ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", Sha))));
    }

    [Fact]
    public void AnEmptyRangeIsRefusedWhenTheArgumentAsksForOne()
    {
        // Fail closed: an empty range would silently scan nothing and report clean.
        Assert.Throws<IOException>(() => ResolveArguments(["{gitLogRange}"]));
    }

    [Fact]
    public void ARangeWithNonHexCharactersIsRefused()
    {
        var notHex = new string('z', 40);

        Assert.Throws<IOException>(() => ResolveArguments(
            ["{gitLogRange}"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", $"{notHex}..{Sha}"))));
    }

    [Fact]
    public void TheRangeIsOnlyValidatedWhenAnArgumentActuallyUsesIt()
    {
        // A scanner that does not read history must not be blocked by a range it ignores.
        var resolved = ResolveArguments(
            ["--quiet"], Env(("AETHEUS_GITLEAKS_LOG_RANGE", "main..develop")));

        Assert.Equal("--quiet", Assert.Single(resolved));
    }
}
