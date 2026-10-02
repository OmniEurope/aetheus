// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>Guards that every static OE form-field label targets a control that exists in its field.</summary>
public class OmniFormFieldAssociationAuditTests
{
    [Fact]
    public void StaticFormFieldTargets_ExistInsideTheirField()
    {
        var front = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Front");
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(front, "*.razor"))
        {
            var source = File.ReadAllText(file);
            foreach (Match field in Regex.Matches(source,
                         @"<OmniFormField\b(?<open>(?:(?:""[^""]*"")|[^>])*)>(?<body>.*?)</OmniFormField>",
                         RegexOptions.Singleline))
            {
                var target = Regex.Match(field.Groups["open"].Value, @"\bFor=""(?<id>[^""@]+)""");
                if (!target.Success) continue;
                var id = Regex.Escape(target.Groups["id"].Value);
                if (!Regex.IsMatch(field.Groups["body"].Value, $@"\bId=""{id}"""))
                    violations.Add($"{Path.GetRelativePath(front, file)}: For=\"{target.Groups["id"].Value}\" has no matching Id");
            }
        }

        Assert.True(violations.Count == 0,
            "Every static OmniFormField For target must identify a control inside the field:\n  "
            + string.Join("\n  ", violations));
    }
}
