// SPDX-License-Identifier: EUPL-1.2
using System.Xml;
using System.Xml.Linq;

namespace Aetheus.Back.Components.Shared;

/// <summary>
/// Hardened XML loading for externally supplied content: DTD and external entities are
/// disabled (XXE-safe) with entity-expansion and document-size caps. Use instead of
/// <see cref="XDocument.Parse(string)"/> / <see cref="XDocument.Load(string)"/> whenever the
/// XML originates outside the server (pipeline test results, uploaded artifacts, …).
/// </summary>
public static class SafeXml
{
    private const int MaxCharactersFromEntities = 1024;
    private const long MaxCharactersInDocument = 8 * 1024 * 1024;

    /// <summary>Document-size cap for TRUSTED agent-uploaded reports (Cobertura coverage, SARIF lint).
    /// A full-solution Cobertura is tens of MB, well past the 8 MB DoS guard for arbitrary XML; these
    /// uploads are authenticated (AgentToken) so a larger cap is acceptable. Still bounded to stop a
    /// runaway allocation.</summary>
    public const long TrustedReportMaxCharacters = 128 * 1024 * 1024;

    /// <summary>Parses <paramref name="xmlContent"/> into an <see cref="XDocument"/> with XXE protections.
    /// <paramref name="maxCharactersInDocument"/> defaults to the strict 8 MB cap for arbitrary XML;
    /// trusted large reports pass <see cref="TrustedReportMaxCharacters"/>.</summary>
    public static XDocument Load(string xmlContent, long maxCharactersInDocument = MaxCharactersInDocument)
        => Load(xmlContent, maxCharactersInDocument, DtdProcessing.Prohibit);

    /// <summary>Parses a trusted agent-generated report while ignoring its standard format DTD.
    /// External resolution and entity expansion remain disabled: a report that references an entity
    /// from the ignored DTD is rejected instead of resolving or expanding it.</summary>
    public static XDocument LoadTrustedReport(string xmlContent)
        => Load(xmlContent, TrustedReportMaxCharacters, DtdProcessing.Ignore);

    private static XDocument Load(
        string xmlContent,
        long maxCharactersInDocument,
        DtdProcessing dtdProcessing)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = dtdProcessing,
            XmlResolver = null,
            MaxCharactersFromEntities = MaxCharactersFromEntities,
            MaxCharactersInDocument = maxCharactersInDocument
        };
        using var stringReader = new StringReader(xmlContent);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader);
    }
}
