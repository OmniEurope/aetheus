// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using System.Text.Json;
using Aetheus.Back.Components.PackageRegistry;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.PackageRegistry;

/// <summary>
/// The npm publish path is the registry's trust boundary: the payload is written by whatever `npm
/// publish` sends. These cover the rules that decide what gets stored - a dist-tag may only point at a
/// version that exists, metadata is bounded, and a scoped name survives URL encoding intact.
/// </summary>
public class NpmRegistryHelperTests
{
    private static readonly Type Service = typeof(INpmRegistryService).Assembly
        .GetType("Aetheus.Back.Components.PackageRegistry.NpmRegistryService")!;

    private static object? Call(string method, params object?[] args)
    {
        var info = Service.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return info.Invoke(null, args);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    // --- package name decoding ---------------------------------------------

    [Theory]
    [InlineData("lodash", "lodash")]
    [InlineData("/lodash/", "lodash")]
    [InlineData("%40scope%2Fpkg", "@scope/pkg")]
    [InlineData("@scope%2fpkg", "@scope/pkg")]
    public void AScopedPackageNameSurvivesUrlEncoding(string route, string expected)
    {
        // npm sends "@scope/pkg" as "@scope%2fpkg"; decoding it wrong splits the scope from the name
        // and publishes under a different package than the client believes.
        Assert.Equal(expected, Call("DecodePackageName", route));
    }

    // --- required strings ---------------------------------------------------

    [Fact]
    public void AMissingRequiredField_IsRejected()
    {
        Assert.Throws<BadRequestException>(() => Call("ReadRequiredString", Json("""{}"""), "name"));
    }

    [Fact]
    public void ARequiredFieldOfTheWrongType_IsRejected()
    {
        Assert.Throws<BadRequestException>(() => Call("ReadRequiredString", Json("""{"name":42}"""), "name"));
    }

    [Fact]
    public void ABlankRequiredField_IsRejected()
    {
        Assert.Throws<BadRequestException>(() => Call("ReadRequiredString", Json("""{"name":"   "}"""), "name"));
    }

    [Fact]
    public void APresentRequiredField_IsReturned()
    {
        Assert.Equal("lodash", Call("ReadRequiredString", Json("""{"name":"lodash"}"""), "name"));
    }

    // --- dist tags ----------------------------------------------------------

    private static RegistryPackage Package(string? distTagsJson = null, params string[] normalizedVersions)
    {
        var package = new RegistryPackage
        {
            Name = "lodash",
            Kind = PackageRegistryKind.Npm,
            DistTagsJson = distTagsJson
        };
        foreach (var version in normalizedVersions)
            package.Versions.Add(new RegistryPackageVersion { NormalizedVersion = version, Version = version });
        return package;
    }

    private static string DistTags(JsonElement root, RegistryPackage package, string normalized, string raw) =>
        (string)Call("ReadAndValidateDistTags", root, package, normalized, raw)!;

    [Fact]
    public void WithNoDistTagsInThePayload_LatestPointsAtTheVersionBeingPublished()
    {
        var json = DistTags(Json("""{}"""), Package(), "2.0.0", "2.0.0");

        Assert.Equal("2.0.0", JsonDocument.Parse(json).RootElement.GetProperty("latest").GetString());
    }

    [Fact]
    public void ADistTagPointingAtTheVersionBeingPublished_IsAccepted()
    {
        var json = DistTags(Json("""{"dist-tags":{"beta":"2.0.0"}}"""), Package(), "2.0.0", "2.0.0");

        Assert.Equal("2.0.0", JsonDocument.Parse(json).RootElement.GetProperty("beta").GetString());
    }

    [Fact]
    public void ADistTagPointingAtAnAlreadyPublishedVersion_IsAccepted()
    {
        var json = DistTags(
            Json("""{"dist-tags":{"stable":"1.0.0"}}"""), Package(null, "1.0.0"), "2.0.0", "2.0.0");

        Assert.Equal("1.0.0", JsonDocument.Parse(json).RootElement.GetProperty("stable").GetString());
    }

    [Fact]
    public void ADistTagPointingAtAnUnknownVersion_IsRejected()
    {
        // Accepting it would leave `npm install pkg@stable` resolving to nothing.
        Assert.Throws<BadRequestException>(() =>
            DistTags(Json("""{"dist-tags":{"stable":"9.9.9"}}"""), Package(), "2.0.0", "2.0.0"));
    }

    [Fact]
    public void ADistTagWhoseValueIsNotAString_IsRejected()
    {
        Assert.Throws<BadRequestException>(() =>
            DistTags(Json("""{"dist-tags":{"latest":42}}"""), Package(), "2.0.0", "2.0.0"));
    }

    [Fact]
    public void ADistTagNameLongerThanSixtyFourCharacters_IsRejected()
    {
        var longName = new string('t', 65);

        Assert.Throws<BadRequestException>(() =>
            DistTags(Json("{\"dist-tags\":{\"" + longName + "\":\"2.0.0\"}}"), Package(), "2.0.0", "2.0.0"));
    }

    [Fact]
    public void ExistingTagsArePreserved_WhenThePayloadDoesNotMentionThem()
    {
        // A publish that only sets "beta" must not silently drop the package's "latest".
        var json = DistTags(
            Json("""{"dist-tags":{"beta":"2.0.0"}}"""),
            Package("""{"latest":"1.0.0"}""", "1.0.0"),
            "2.0.0",
            "2.0.0");

        var tags = JsonDocument.Parse(json).RootElement;
        Assert.Equal("1.0.0", tags.GetProperty("latest").GetString());
        Assert.Equal("2.0.0", tags.GetProperty("beta").GetString());
    }

    [Fact]
    public void ADistTagIsMatchedOnTheNormalizedVersion_NotTheRawString()
    {
        // "1.0" and "1.0.0" are the same version; rejecting the tag on a string comparison would
        // refuse a legitimate publish.
        var json = DistTags(
            Json("""{"dist-tags":{"stable":"1.0"}}"""), Package(null, "1.0.0"), "2.0.0", "2.0.0");

        Assert.Equal("1.0", JsonDocument.Parse(json).RootElement.GetProperty("stable").GetString());
    }

    // --- metadata bound -----------------------------------------------------

    [Fact]
    public void MetadataWithinTheBound_IsStoredVerbatim()
    {
        var element = Json("""{"a":"b"}""");

        Assert.Equal("""{"a":"b"}""", Call("ReadBoundedMetadata", element));
    }

    [Fact]
    public void MetadataBeyondTheBound_IsRejectedRatherThanTruncated()
    {
        // Truncating would store invalid JSON; the publish must fail instead.
        var oversized = Json("{\"pad\":\"" + new string((char)120, 1024 * 1024 + 16) + "\"}");

        Assert.Throws<BadRequestException>(() => Call("ReadBoundedMetadata", oversized));
    }

    // --- new-version selection ------------------------------------------------

    private static (string Version, JsonElement Document) FindNew(JsonElement root, RegistryPackage? existing)
    {
        var info = Service.GetMethod("FindNewVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return ((string, JsonElement))info.Invoke(null, [root, existing])!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    [Fact]
    public void APublishWithNoVersionsObject_IsRejected()
    {
        Assert.Throws<BadRequestException>(() => FindNew(Json("""{}"""), null));
    }

    [Fact]
    public void APublishIntroducingExactlyOneVersion_IsAccepted()
    {
        var (version, _) = FindNew(Json("""{"versions":{"1.2.3":{"name":"lodash"}}}"""), null);

        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void RepublishingAnExistingVersion_IsAConflict_NotAnOverwrite()
    {
        // npm treats a published version as immutable; silently overwriting would change what every
        // existing lockfile resolves to.
        Assert.Throws<ConflictException>(() =>
            FindNew(Json("""{"versions":{"1.0.0":{"name":"lodash"}}}"""), Package(null, "1.0.0")));
    }

    [Fact]
    public void AnExistingVersionIsRecognisedThroughNormalisation()
    {
        // "1.0" and "1.0.0" are the same version; comparing raw strings would let the same release be
        // published twice under two spellings.
        Assert.Throws<ConflictException>(() =>
            FindNew(Json("""{"versions":{"1.0":{"name":"lodash"}}}"""), Package(null, "1.0.0")));
    }

    [Fact]
    public void APublishIntroducingTwoNewVersionsAtOnce_IsRejected()
    {
        Assert.Throws<BadRequestException>(() => FindNew(
            Json("""{"versions":{"1.0.0":{"n":1},"2.0.0":{"n":2}}}"""), null));
    }

    [Fact]
    public void AVersionWhoseDocumentIsNotAnObject_IsRejected()
    {
        Assert.Throws<BadRequestException>(() => FindNew(Json("""{"versions":{"1.0.0":"oops"}}"""), null));
    }

    [Fact]
    public void OnlyTheUnpublishedVersionIsSelected_WhenThePayloadRepeatsOldOnes()
    {
        // npm clients resend the full version history; only the genuinely new one may be taken.
        var (version, _) = FindNew(
            Json("""{"versions":{"1.0.0":{"n":1},"2.0.0":{"n":2}}}"""), Package(null, "1.0.0"));

        Assert.Equal("2.0.0", version);
    }

    // --- selector resolution and unlisted visibility --------------------------

    private static RegistryPackageVersion Version(string version, bool listed = true) => new()
    {
        Version = version,
        NormalizedVersion = version,
        IsListed = listed
    };

    private static RegistryPackage PackageOf(string? distTagsJson, params RegistryPackageVersion[] versions)
    {
        var package = new RegistryPackage { Name = "lodash", Kind = PackageRegistryKind.Npm, DistTagsJson = distTagsJson };
        foreach (var version in versions) package.Versions.Add(version);
        return package;
    }

    private static RegistryPackageVersion? Resolve(RegistryPackage package, string selector)
    {
        var info = Service.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .First(m => m.Name == "ResolveSelector"
                && m.GetParameters()[0].ParameterType == typeof(RegistryPackage));
        return (RegistryPackageVersion?)info.Invoke(null, [package, selector]);
    }

    private static System.Text.Json.Nodes.JsonObject VisibleTags(RegistryPackage package) =>
        (System.Text.Json.Nodes.JsonObject)Service
            .GetMethod("BuildVisibleDistTags", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [package])!;

    [Fact]
    public void AnExactVersionSelectorResolvesToThatVersion()
    {
        Assert.Equal("1.0.0", Resolve(PackageOf(null, Version("1.0.0")), "1.0.0")!.Version);
    }

    [Fact]
    public void ATagSelectorResolvesThroughTheDistTag()
    {
        var package = PackageOf("{\"latest\":\"2.0.0\"}", Version("1.0.0"), Version("2.0.0"));

        Assert.Equal("2.0.0", Resolve(package, "latest")!.Version);
    }

    [Fact]
    public void AnUnknownSelectorResolvesToNothing()
    {
        Assert.Null(Resolve(PackageOf(null, Version("1.0.0")), "does-not-exist"));
    }

    [Fact]
    public void AnUnlistedVersionIsNeverResolved()
    {
        // Unlisting is how a bad release is withdrawn; still serving it by exact version would defeat
        // the withdrawal.
        Assert.Null(Resolve(PackageOf(null, Version("1.0.0", listed: false)), "1.0.0"));
    }

    [Fact]
    public void ADistTagPointingAtAnUnlistedVersion_IsHiddenFromThePackument()
    {
        // Advertising a tag that resolves to a withdrawn version would make npm install fail.
        var package = PackageOf("{\"beta\":\"1.0.0\"}", Version("1.0.0", listed: false), Version("2.0.0"));

        Assert.Null(VisibleTags(package)["beta"]);
    }

    [Fact]
    public void WhenNoTagNamesLatest_TheHighestListedVersionBecomesLatest()
    {
        var package = PackageOf(null, Version("1.0.0"), Version("2.1.0"), Version("2.0.0"));

        Assert.Equal("2.1.0", VisibleTags(package)["latest"]!.GetValue<string>());
    }

    [Fact]
    public void AnExplicitLatestTagIsNotOverriddenByTheHighestVersion()
    {
        var package = PackageOf("{\"latest\":\"1.0.0\"}", Version("1.0.0"), Version("2.0.0"));

        Assert.Equal("1.0.0", VisibleTags(package)["latest"]!.GetValue<string>());
    }

    [Fact]
    public void APackageWithOnlyUnlistedVersions_AdvertisesNoLatest()
    {
        var package = PackageOf(null, Version("1.0.0", listed: false));

        Assert.Null(VisibleTags(package)["latest"]);
    }
}
