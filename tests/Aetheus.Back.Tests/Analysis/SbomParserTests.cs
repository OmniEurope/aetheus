// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Analysis;

public sealed class SbomParserTests
{
    [Fact]
    public void CycloneDxJson_ExtractsPackageUrlLicenseAndHash()
    {
        var components = CycloneDxParser.Parse("""
            {
              "bomFormat": "CycloneDX",
              "components": [{
                "type": "library",
                "name": "YamlDotNet",
                "version": "16.3.0",
                "purl": "pkg:nuget/YamlDotNet@16.3.0",
                "scope": "required",
                "licenses": [{ "license": { "id": "MIT" } }],
                "hashes": [{ "alg": "SHA-256", "content": "abc" }]
              }]
            }
            """, AnalysisReportFormat.CycloneDxJson);

        var component = Assert.Single(components);
        Assert.Equal("YamlDotNet", component.Name);
        Assert.Equal("pkg:nuget/YamlDotNet@16.3.0", component.PackageUrl);
        Assert.Equal(["MIT"], component.Licenses);
        Assert.Equal("SHA-256:abc", component.Hash);
        Assert.True(component.IsDirect);
    }

    [Fact]
    public void CycloneDxXml_RejectsExternalEntity()
    {
        var xml = """
            <!DOCTYPE bom [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <bom><components><component type="library"><name>&xxe;</name></component></components></bom>
            """;

        Assert.Throws<BadRequestException>(() => CycloneDxParser.Parse(xml, AnalysisReportFormat.CycloneDxXml));
    }

    [Fact]
    public void SpdxJson_ExtractsPurlAndDeclaredLicense()
    {
        var components = SpdxJsonParser.Parse("""
            {
              "spdxVersion": "SPDX-2.3",
              "packages": [{
                "name": "eslint",
                "versionInfo": "9.0.0",
                "licenseConcluded": "NOASSERTION",
                "licenseDeclared": "MIT",
                "externalRefs": [{ "referenceType": "purl", "referenceLocator": "pkg:npm/eslint@9.0.0" }],
                "checksums": [{ "algorithm": "SHA256", "checksumValue": "def" }]
              }]
            }
            """);

        var component = Assert.Single(components);
        Assert.Equal("pkg:npm/eslint@9.0.0", component.PackageUrl);
        Assert.Equal(["MIT"], component.Licenses);
        Assert.Equal("SHA256:def", component.Hash);
    }
}
