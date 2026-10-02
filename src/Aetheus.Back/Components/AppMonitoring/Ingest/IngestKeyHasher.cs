// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.AppMonitoring.Ingest;

/// <summary>
/// Generates and hashes OTLP ingestion keys. Uses the same keyed HMAC-SHA256 primitive as every other
/// DB-resolved token in the codebase (ServerToken / RegistrationToken / RefreshToken) so the plaintext
/// is never stored - only <see cref="Data.Entities.MonitoredApp.IngestKeyHash"/>.
/// </summary>
public sealed class IngestKeyHasher(IOptions<JwtOptions> jwtOptions)
{
    private readonly string _signingKey = jwtOptions.Value.SigningKey;

    public string Generate() => AuthTokenHelper.GenerateSecureToken();

    public string Hash(string key) => AuthTokenHelper.HashToken(_signingKey, key);

    /// <summary>
    /// The ingestion key a deployment run hands its containers, derived rather than drawn so that
    /// every backend instance computes the same one. It used to be random and remembered in process
    /// memory, and a production deployment restarts the backend it runs on: the new colour forgot the
    /// key and rotated again within the same run (runs 2334, 2327 twice, 2325 three times, 1979 nine
    /// times in the audit log), leaving the containers on a key that expires with the overlap window.
    /// Distinct purpose label, same secret as <see cref="Hash"/>: whoever could derive it could
    /// already sign an access token.
    /// </summary>
    public string DeriveForRun(int appId, int pipelineRunId) =>
        AuthTokenHelper.HashToken(
            _signingKey,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"aetheus:ingest-key:deploy-run:v1:{appId}:{pipelineRunId}"));
}
