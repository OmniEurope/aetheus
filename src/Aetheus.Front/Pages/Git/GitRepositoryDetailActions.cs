// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

internal sealed class GitRepositoryDetailActions(
    ApiClient api,
    DialogService dialog,
    NotifyHelper toast,
    IStringLocalizer<AppStrings> localizer)
{
    public async Task<bool> CreateBranchAsync(int repositoryId)
    {
        var result = await dialog.OpenAsync<GitBranchCreateDialog>(
            localizer["NewBranch"].Value,
            new Dictionary<string, object?>
            {
                ["RepoId"] = repositoryId,
                ["Branches"] = await api.GetGitBranchesAsync(repositoryId)
            },
            new DialogOptions { Width = "400px" });
        return result is true;
    }

    public async Task<bool> DeleteBranchAsync(int repositoryId, GitLightBranchDto branch)
    {
        var confirmed = await ConfirmDeleteAsync(branch.Name);
        if (!confirmed) return false;
        var success = await api.DeleteGitBranchAsync(repositoryId, branch.Name);
        if (success) toast.Success("Deleted", branch.Name);
        return success;
    }

    public async Task<bool> CreateTagAsync(int repositoryId)
    {
        var result = await dialog.OpenAsync<GitTagCreateDialog>(
            localizer["NewTag"].Value,
            new Dictionary<string, object?>
            {
                ["RepoId"] = repositoryId,
                ["Branches"] = await api.GetGitBranchesAsync(repositoryId)
            },
            new DialogOptions { Width = "400px" });
        return result is true;
    }

    public async Task<bool> DeleteTagAsync(int repositoryId, GitLightTagDto tag)
    {
        var confirmed = await ConfirmDeleteAsync(tag.Name);
        if (!confirmed) return false;
        var success = await api.DeleteGitTagAsync(repositoryId, tag.Name);
        if (success) toast.Success("Deleted", tag.Name);
        return success;
    }

    public async Task<bool> CreatePullRequestAsync(int repositoryId)
    {
        var result = await dialog.OpenAsync<GitPrCreateDialog>(
            localizer["NewPullRequest"].Value,
            new Dictionary<string, object?>
            {
                ["RepoId"] = repositoryId,
                ["Branches"] = await api.GetGitBranchesAsync(repositoryId)
            },
            new DialogOptions { Width = "500px" });
        return result is true;
    }

    public async Task<bool> MergePullRequestAsync(
        int repositoryId, InternalPullRequestDto pullRequest)
    {
        var confirmed = await dialog.Confirm(
            string.Format(localizer["MergeConfirm"].Value, pullRequest.Number),
            localizer["Merge"].Value,
            new ConfirmOptions
            {
                OkButtonText = localizer["Merge"].Value,
                CancelButtonText = localizer["Cancel"].Value
            });
        if (confirmed != true) return false;
        var result = await api.MergeGitPullRequestAsync(repositoryId, pullRequest.Number);
        if (result is not null) toast.Success("Merged", $"#{pullRequest.Number}");
        return result is not null;
    }

    public async Task<bool> ClosePullRequestAsync(
        int repositoryId, InternalPullRequestDto pullRequest)
    {
        var confirmed = await dialog.Confirm(
            string.Format(localizer["CloseConfirm"].Value, pullRequest.Number),
            localizer["Close"].Value,
            new ConfirmOptions
            {
                OkButtonText = localizer["Close"].Value,
                CancelButtonText = localizer["Cancel"].Value
            });
        if (confirmed != true) return false;
        var result = await api.CloseGitPullRequestAsync(repositoryId, pullRequest.Number);
        if (result is not null) toast.Success("Closed", $"#{pullRequest.Number}");
        return result is not null;
    }

    public async Task<bool> AddProtectionRuleAsync(int repositoryId)
    {
        var result = await dialog.OpenAsync<GitBranchProtectionDialog>(
            localizer["AddRule"].Value,
            new Dictionary<string, object?> { ["RepoId"] = repositoryId },
            new DialogOptions { Width = "450px" });
        return result is true;
    }

    public async Task<bool> DeleteProtectionRuleAsync(
        int repositoryId, BranchProtectionRuleDto rule)
    {
        var confirmed = await ConfirmDeleteAsync(rule.Pattern);
        if (!confirmed) return false;
        var success = await api.DeleteGitBranchProtectionRuleAsync(repositoryId, rule.Id);
        if (success) toast.Success("Deleted", rule.Pattern);
        return success;
    }

    public async Task<bool> UpdateProtectionRuleAsync(
        int repositoryId,
        BranchProtectionRuleDto rule,
        bool? preventDeletion,
        bool? preventForcePush,
        bool? requirePullRequest)
    {
        var result = await api.UpdateGitBranchProtectionRuleAsync(
            repositoryId,
            rule.Id,
            new UpdateBranchProtectionRuleRequest
            {
                PreventDeletion = preventDeletion ?? rule.PreventDeletion,
                PreventForcePush = preventForcePush ?? rule.PreventForcePush,
                RequirePullRequest = requirePullRequest ?? rule.RequirePullRequest
            });
        return result is not null;
    }

    private async Task<bool> ConfirmDeleteAsync(string name) =>
        await dialog.Confirm(
            string.Format(localizer["DeleteConfirm"].Value, name),
            localizer["Delete"].Value,
            new ConfirmOptions
            {
                OkButtonText = localizer["Delete"].Value,
                CancelButtonText = localizer["Cancel"].Value
            }) == true;
}
