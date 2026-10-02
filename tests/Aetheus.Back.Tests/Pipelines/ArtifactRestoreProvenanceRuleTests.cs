// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Which restores must prove their artifact's provenance (backlog defect D-04).
///
/// The rule matters more than it looks: a restore that skips the check consumes another commit's
/// build in silence, and every consumer used to have to remember a separate shell stage to catch
/// that. Getting the exception wrong in the other direction is just as bad, though quieter, because
/// it refuses every baseline comparison the deploy pipelines depend on.
/// </summary>
public sealed class ArtifactRestoreProvenanceRuleTests
{
    private const string RunCommit = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0";

    private static Dictionary<string, string> Variables(string? commit = RunCommit)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (commit is not null) variables["BUILD_SOURCEVERSION"] = commit;
        return variables;
    }

    [Fact]
    public void AnOrdinaryRestoreMustProveTheRunsOwnRevision()
    {
        Assert.Equal(
            RunCommit,
            PipelineArtifactTaskFactory.ExpectedSourceCommitFor(Variables(), releaseSelector: null));
    }

    [Fact]
    public void ASiblingPipelineRestoreMustProveItToo()
    {
        Assert.Equal(
            RunCommit,
            PipelineArtifactTaskFactory.ExpectedSourceCommitFor(Variables(), releaseSelector: ""));
    }

    [Theory]
    [InlineData("latest-published")]
    [InlineData("previous-published")]
    [InlineData("previous-deployed")]
    [InlineData("current-deployed")]
    [InlineData("PREVIOUS-DEPLOYED")]
    public void ARestoreOfWhatIsPublishedOrDeployedIsExempt(string selector)
    {
        // These deliberately restore another commit: comparing against it is the point.
        Assert.Null(PipelineArtifactTaskFactory.ExpectedSourceCommitFor(Variables(), selector));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARunThatDeclaresNoRevisionHasNothingToCompareAgainst(string? commit)
    {
        Assert.Null(
            PipelineArtifactTaskFactory.ExpectedSourceCommitFor(
                Variables(commit), releaseSelector: null));
    }

    [Theory]
    [InlineData("c-384f63d4a8c182e07de2e9444c25e0a5a9a28e8e")]
    [InlineData("v1.2.3")]
    public void ARestoreOfANamedReleaseIsExemptToo(string namedRelease)
    {
        // A named release names a build, and a build carries its own revision. Exempting only the
        // symbolic selectors made aetheus-deploy-prod work solely while develop had not moved since
        // the candidate was qualified: run 2323 promoted c-384f63d4 from develop at 61a6b12a and
        // every candidate restore was refused with "built from 384f63d4, but this run was launched
        // on 61a6b12a". Ancestry between the two is verify-release-ancestry.sh's job, not this
        // check's.
        Assert.Null(PipelineArtifactTaskFactory.ExpectedSourceCommitFor(Variables(), namedRelease));
    }

    [Theory]
    [InlineData("latest-successful")]
    [InlineData("LATEST-SUCCESSFUL")]
    [InlineData(" latest-successful ")]
    public void ARestoreOfTheNewestSuccessfulRunIsExempt(string selector)
    {
        // latest-successful takes the newest successful run of another pipeline, which was built
        // from whatever commit that pipeline ran on - never this run's. Demanding this run's
        // revision refused every such restore: aetheus-deploy-prod 2321 died on the nightly
        // qualification verdict with "the restored artifact carries no source-commit", and no
        // production deployment could complete.
        Assert.Null(PipelineArtifactTaskFactory.ExpectedSourceCommitFor(
            Variables(), releaseSelector: null, artifactSourceSelector: selector));
    }

    [Fact]
    public void TheDefaultSameCommitSelectorStillDemandsThisRunsRevision()
    {
        // The exemption above is opt-in and must not widen to the default resolution, which is the
        // one that proves a consumer graded the build it was handed.
        Assert.Equal(
            RunCommit,
            PipelineArtifactTaskFactory.ExpectedSourceCommitFor(
                Variables(), releaseSelector: null, artifactSourceSelector: "same-commit"));
        Assert.Equal(
            RunCommit,
            PipelineArtifactTaskFactory.ExpectedSourceCommitFor(
                Variables(), releaseSelector: null, artifactSourceSelector: null));
    }

    [Fact]
    public void AnUnrecognisedReleaseNameNeverReachesThisRuleAtAll()
    {
        // This assertion is deliberately the opposite of what it used to be, and the reason matters.
        // It used to demand this run's revision for an unrecognised selector, on a fail-closed
        // argument. But `release:` is not an enum: ResolveReleaseArtifactAsync looks up anything
        // that is not one of the four symbolic selectors as an actual release name, and an unknown
        // name fails there with "release '...' has no retained artifact in this project". A
        // misspelled selector therefore never reaches task creation, so the old rule protected a
        // case that cannot happen while refusing the one that does: promoting a named candidate
        // release from a develop that has moved on (run 2323).
        //
        // The fail-closed intent still holds where it is real: artifact_source_selector IS a closed
        // set, validated by ResolveSelector, and an unknown value there is an error.
        Assert.Null(
            PipelineArtifactTaskFactory.ExpectedSourceCommitFor(Variables(), "some-new-selector"));
    }
}
