// SPDX-License-Identifier: EUPL-1.2
using System.Xml.Linq;

namespace Aetheus.Front.Tests.Architecture;

public class AiRunnerProfileLocalizationTests
{
    [Fact]
    public void EnvironmentFormatFailure_UsesLocalizedResourceInsteadOfExceptionMessage()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Aetheus.Front",
            "Pages",
            "Ai",
            "AiRunnerProfileDialog.razor.cs"));

        Assert.Contains("Notify.Error(\"ValidationError\", \"AiEnvironmentFormatError\")", source);
        Assert.DoesNotContain("ex.Message", source);

        Assert.Equal(
            "Each environment variable must use the NAME=VALUE format.",
            ResourceValue(root, "AppStrings.resx", "AiEnvironmentFormatError"));
        Assert.Equal(
            "Chaque variable d’environnement doit suivre le format NOM=VALEUR.",
            ResourceValue(root, "AppStrings.fr-FR.resx", "AiEnvironmentFormatError"));
    }

    private static string ResourceValue(string root, string fileName, string key)
    {
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "Aetheus.Front",
            "Resources",
            fileName));
        return document.Root!
            .Elements("data")
            .Single(element => (string?)element.Attribute("name") == key)
            .Element("value")!
            .Value;
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
