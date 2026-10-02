// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.AiTasks;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Ai;

/// <summary>
/// The apply button of an AI patch. It writes the patch to a proposed branch, never to the checked-out
/// one: its short label says so ("Appliquer (branche)", D36 of PLAN-005), the full sentence is its title.
/// </summary>
public sealed class AiResultDialogTests : BunitContext
{
    public AiResultDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void ThePatchApplyButton_NamesTheBranch_AndExplainsItInItsTitle()
    {
        var result = new AiRunResultDto
        {
            Id = 7,
            ProfileName = "reviewer",
            ReportMarkdown = "report",
            DiffPatch = "--- a/x\n+++ b/x\n",
            ProjectId = 3,
            Succeeded = true
        };
        var cut = Render<AiResultDialog>(p => p.Add(x => x.InitialResult, result));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='tab']")));
        cut.FindAll("[role='tab']").Single(b => b.TextContent.Contains("Diff", StringComparison.Ordinal)).Click();

        var apply = cut.WaitForElement("button[title='ApplyOnProposedBranch']");
        Assert.Contains("ApplyToBranch", apply.TextContent, StringComparison.Ordinal);
    }
}
