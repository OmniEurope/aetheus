// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC008 reports a certificate callback that accepts everything and the handler's named
/// accept-any validator, and leaves a callback that actually decides alone.
/// </summary>
public sealed class Sec008Tests
{
    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC008_CertificateValidationStaysOnAnalyzer>.VerifyAsync(source);

    [Fact]
    public async Task AnAcceptAnyCallbackIsReportedInEveryShape()
    {
        await VerifyAsync("""
            using System.Net.Http;
            using System.Net.Security;
            public static class Handlers
            {
                public static HttpClientHandler Lambda() => new()
                {
                    ServerCertificateCustomValidationCallback = {|SEC008:(_, _, _, _) => true|}
                };

                public static SslClientAuthenticationOptions Block() => new()
                {
                    RemoteCertificateValidationCallback = {|SEC008:(sender, certificate, chain, errors) => { return true; }|}
                };

                public static RemoteCertificateValidationCallback Anonymous() => {|SEC008:delegate { return true; }|};

                public static HttpClientHandler Named() => new()
                {
                    ServerCertificateCustomValidationCallback = {|SEC008:HttpClientHandler.DangerousAcceptAnyServerCertificateValidator|}
                };
            }
            """);
    }

    [Fact]
    public async Task ACallbackThatDecidesIsNotReported()
    {
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            using System.Net.Security;
            using System.Security.Cryptography.X509Certificates;
            public static class Handlers
            {
                public static HttpClientHandler Pinned(string thumbprint) => new()
                {
                    ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                        errors == SslPolicyErrors.None
                        || string.Equals(certificate?.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)
                };

                // A bool-returning lambda that is not a certificate callback is not this rule's business.
                public static Func<int, bool> Unrelated() => _ => true;
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_AnAcceptAnyCallbackBehindAGuardElsewhereIsStillReported()
    {
        // Documented false positive: the switch is refused at startup outside loopback by another
        // method, which the rule cannot see. Production code suppresses it with the guard's name.
        await VerifyAsync("""
            using System.Net.Security;
            public static class Tls
            {
                public static RemoteCertificateValidationCallback? For(bool allowInsecureOnLoopback) =>
                    allowInsecureOnLoopback ? {|SEC008:(_, _, _, _) => true|} : null;
            }
            """);
    }
}
