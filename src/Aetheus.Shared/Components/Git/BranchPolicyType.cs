// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Git;

public enum BranchPolicyType
{
    RequirePullRequest = 0,
    MinimumReviewers = 1,
    BuildValidation = 2,
    StatusCheck = 3,
    CommentResolution = 4
}
