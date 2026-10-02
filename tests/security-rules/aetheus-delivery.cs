// SPDX-License-Identifier: EUPL-1.2
// Test corpus for .aetheus/security-rules/opengrep/aetheus-delivery.yml, C# rules. Not compiled.
public static class DeliveryRuleCorpus
{
    public static void Configure(HttpClientHandler handler, SslStream stream)
    {
        // ruleid: aetheus.csharp.disabled-tls-validation
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        // ruleid: aetheus.csharp.disabled-tls-validation
        handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
        // ok: aetheus.csharp.disabled-tls-validation
        handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => errors == SslPolicyErrors.None;

        // ruleid: aetheus.csharp.binary-formatter
        var formatter = new BinaryFormatter();
        // ok: aetheus.csharp.binary-formatter
        var model = JsonSerializer.Deserialize<Model>(payload);
    }
}
