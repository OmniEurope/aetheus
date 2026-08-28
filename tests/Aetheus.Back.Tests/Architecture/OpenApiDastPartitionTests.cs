// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Tests.Architecture;

public sealed class OpenApiDastPartitionTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const int PartitionCount = global::Aetheus.Back.OpenApiDastPartition.Count;
    private static readonly HashSet<string> HttpMethods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    [Fact]
    public async Task DastDocumentsPartitionTheCompleteOpenApiSurface()
    {
        using var client = factory.CreateClient();
        var completePayload = await client.GetStringAsync(
            "/openapi/v1.json", TestContext.Current.CancellationToken);
        using var complete = JsonDocument.Parse(completePayload);
        var expected = Operations(complete);
        var partitions = new List<HashSet<string>>(PartitionCount);
        for (var partition = 0; partition < PartitionCount; partition++)
        {
            var partitionPayload = await client.GetStringAsync(
                $"/openapi/dast-{partition}.json", TestContext.Current.CancellationToken);
            Assert.True(
                partitionPayload.Length < completePayload.Length / 3,
                $"DAST partition {partition} is too large to bound scanner memory.");
            using var document = JsonDocument.Parse(partitionPayload);
            AssertLocalReferencesResolve(document);
            partitions.Add(Operations(document));
        }

        var union = new HashSet<string>(StringComparer.Ordinal);
        for (var partition = 0; partition < partitions.Count; partition++)
        {
            Assert.NotEmpty(partitions[partition]);
            Assert.InRange(
                partitions[partition].Count,
                expected.Count / (PartitionCount * 2),
                expected.Count * 2 / PartitionCount);
            for (var other = partition + 1; other < partitions.Count; other++)
            {
                Assert.Empty(partitions[partition].Intersect(partitions[other], StringComparer.Ordinal));
            }

            union.UnionWith(partitions[partition]);
        }

        Assert.True(expected.SetEquals(union));
    }

    private static HashSet<string> Operations(JsonDocument document)
    {
        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (HttpMethods.Contains(operation.Name))
                {
                    operations.Add($"{operation.Name}:{path.Name}");
                }
            }
        }

        return operations;
    }

    private static void AssertLocalReferencesResolve(JsonDocument document)
    {
        foreach (var reference in LocalReferences(document.RootElement))
        {
            Assert.StartsWith("#/", reference);
            var current = document.RootElement;
            foreach (var rawSegment in reference[2..].Split('/'))
            {
                var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                Assert.True(
                    current.TryGetProperty(segment, out var resolved),
                    $"OpenAPI reference '{reference}' does not resolve at '{segment}'.");
                current = resolved;
            }
        }
    }

    private static IEnumerable<string> LocalReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                {
                    yield return property.Value.GetString()!;
                }

                foreach (var reference in LocalReferences(property.Value))
                {
                    yield return reference;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var reference in LocalReferences(item))
                {
                    yield return reference;
                }
            }
        }
    }
}
