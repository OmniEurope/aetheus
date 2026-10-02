// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// <c>EnumLocalizationHelper</c> builds the key <c>Enum_{Type}_{Value}</c> and the localizer falls back
/// to the key itself when it is absent, so a forgotten value does not fail anything: it ships, and the
/// badge reads ENUM_PIPELINESTATUS_WAITINGFORAPPROVAL in production. That is exactly what happened to
/// <see cref="PipelineStatus.WaitingForApproval"/>, added to the enum without either resource file.
///
/// The enums under guard are DISCOVERED from the resource file, never listed here: a hand-kept list is
/// the same failure mode one level up, silently guarding two types while twenty others go unchecked.
/// Every <c>Enum_{Type}_</c> prefix present in the English file marks that enum as localized, and every
/// one of its values must then carry a key in both cultures. This catches the observed bug exactly - a
/// value added to an enum the UI already localizes - and grows on its own as enums are added.
/// </summary>
public partial class EnumLocalizationCompletenessTests
{
    /// <summary>Enum types the resource file shows as localized, resolved against the shared enum assembly.</summary>
    public static TheoryData<Type> LocalizedEnums()
    {
        var data = new TheoryData<Type>();
        foreach (var type in DiscoverLocalizedEnums()) data.Add(type);
        return data;
    }

    private static List<Type> DiscoverLocalizedEnums()
    {
        var assembly = typeof(PipelineStatus).Assembly;
        return Keys("AppStrings.resx")
            .Select(key => EnumKey().Match(key))
            .Where(match => match.Success)
            .Select(match => match.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .Select(name => assembly.GetTypes().FirstOrDefault(t => t.IsEnum && t.Name == name))
            .OfType<Type>()
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"^Enum_(?<type>[A-Za-z0-9]+)_(?<value>[A-Za-z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EnumKey();

    [Fact]
    public void TheDiscovery_FindsMoreThanAHandfulOfEnums()
    {
        // A discovery that silently resolves nothing would make every case below vacuously pass, which is
        // precisely the shape of guard this test replaced. Pin that it actually finds the UI's enums.
        var discovered = DiscoverLocalizedEnums();

        Assert.Contains(typeof(PipelineStatus), discovered);
        Assert.Contains(typeof(TaskExecutionStatus), discovered);
        Assert.True(discovered.Count >= 5, $"Only {discovered.Count} localized enum(s) discovered.");
    }

    [Theory]
    [MemberData(nameof(LocalizedEnums))]
    public void EveryEnumValue_HasAKeyInBothCultures(Type enumType)
    {
        var english = Keys("AppStrings.resx");
        var french = Keys("AppStrings.fr-FR.resx");

        var missing = Enum.GetNames(enumType)
            .Select(value => $"Enum_{enumType.Name}_{value}")
            .SelectMany(key => new[]
            {
                english.Contains(key) ? null : $"AppStrings.resx: {key}",
                french.Contains(key) ? null : $"AppStrings.fr-FR.resx: {key}"
            })
            .OfType<string>()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void TheStatusThatShippedUntranslated_IsCovered()
    {
        // Pins the specific regression rather than trusting the sweep above to keep listing the enum.
        Assert.Contains("Enum_PipelineStatus_WaitingForApproval", Keys("AppStrings.resx"));
        Assert.Contains("Enum_PipelineStatus_WaitingForApproval", Keys("AppStrings.fr-FR.resx"));
    }

    private static HashSet<string> Keys(string fileName)
    {
        var document = XDocument.Load(Path.Combine(
            RepositoryScan.Root, "src", "Aetheus.Front", "Resources", fileName));
        return document.Root!
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }
}
