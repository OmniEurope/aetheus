// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.PackageRegistry;

[ApiController]
[Route("api/packages/npm")]
[Authorize(AuthenticationSchemes = PackageRegistryAuthenticationHandler.SchemeName)]
public class NpmRegistryController(
    INpmRegistryService registry,
    PackageRegistryUploadGate uploadGate,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("-/ping")]
    public IActionResult Ping() => Ok(new { ok = true });

    [HttpGet("-/v1/search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? text,
        [FromQuery] int from = 0,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var result = await registry.SearchAsync(text, from, size, ct);
        return Ok(new
        {
            objects = result.Packages.Select(package => new
            {
                package = new
                {
                    name = package.Name,
                    version = package.Version,
                    description = package.Description,
                    date = package.PublishedAt,
                    links = new { npm = $"{RegistryBaseUrl}/{EncodePackagePath(package.Name)}" }
                },
                score = new { final = 1.0, detail = new { quality = 1.0, popularity = 0.0, maintenance = 1.0 } },
                searchScore = 1.0
            }),
            total = result.Total,
            time = 0
        });
    }

    [HttpGet("{**path}")]
    public async Task<IActionResult> GetPackage(string path, CancellationToken ct)
    {
        var decodedPath = DecodePath(path);
        var tarballMarker = decodedPath.IndexOf("/-/", StringComparison.Ordinal);
        if (tarballMarker >= 0)
        {
            var packageName = decodedPath[..tarballMarker];
            var fileName = decodedPath[(tarballMarker + 3)..];
            var version = await registry.GetTarballAsync(packageName, fileName, ct);
            if (version is null)
                return NotFound();
            var stream = registry.OpenContent(version);
            return stream is null
                ? NotFound()
                : File(stream, PackageRegistryDefaults.NpmContentType, enableRangeProcessing: true);
        }

        var (name, selector) = SplitPackageAndSelector(decodedPath);
        if (selector is null)
        {
            var packument = await registry.GetPackumentAsync(name, RegistryBaseUrl, ct);
            return packument is null ? NotFound() : Ok(packument);
        }

        var selectedVersion = await registry.GetVersionAsync(name, selector, RegistryBaseUrl, ct);
        return selectedVersion is null ? NotFound() : Ok(selectedVersion);
    }

    [HttpPut("{**path}")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(PackageRegistryDefaults.MaxRequestBodyBytes)]
    public async Task<IActionResult> Publish(string path, CancellationToken ct)
    {
        using var uploadLease = uploadGate.TryEnter();
        if (uploadLease is null)
            return StatusCode(StatusCodes.Status429TooManyRequests);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return BadRequest("Malformed npm publish document.");
        }

        using (document)
        {
            await registry.PublishAsync(
                DecodePath(path),
                document,
                User.Identity?.Name ?? "unknown",
                ct).ConfigureAwait(false);
        }
        return Created($"{RegistryBaseUrl}/{EncodePackagePath(DecodePath(path))}", new
        {
            ok = true,
            id = DecodePath(path),
            rev = timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString()
        });
    }

    private string RegistryBaseUrl
        => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/packages/npm";

    private static (string Name, string? Selector) SplitPackageAndSelector(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return (path, null);
        var nameLength = segments[0].StartsWith('@') ? 2 : 1;
        if (segments.Length <= nameLength)
            return (string.Join('/', segments), null);
        return (string.Join('/', segments.Take(nameLength)), string.Join('/', segments.Skip(nameLength)));
    }

    private static string DecodePath(string path)
        => Uri.UnescapeDataString(path.Trim('/'))
            .Replace("%2f", "/", StringComparison.OrdinalIgnoreCase);

    private static string EncodePackagePath(string packageName)
        => string.Join('/', packageName.Split('/').Select(Uri.EscapeDataString));
}
