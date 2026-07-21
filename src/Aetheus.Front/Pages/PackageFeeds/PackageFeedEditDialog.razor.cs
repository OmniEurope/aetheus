// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.PackageFeeds;

public partial class PackageFeedEditDialog : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private readonly EditModel _model = new();
    private bool _busy;

    private static readonly List<object> _types = Enum.GetValues<PackageFeedType>()
        .Select(t => (object)new { Text = t.ToString(), Value = t }).ToList();

    // Prefill the upstream URL with the public registry default for the chosen type (editable).
    private void OnTypeChanged() => _model.UpstreamUrl = _model.FeedType switch
    {
        PackageFeedType.NuGet => PackageRegistryDefaults.NuGet,
        PackageFeedType.Npm => PackageRegistryDefaults.Npm,
        PackageFeedType.PyPI => PackageRegistryDefaults.PyPi,
        _ => _model.UpstreamUrl
    };

    private async Task SubmitAsync()
    {
        _busy = true;
        try
        {
            await Ui.RunAsync(
                () => Api.CreatePackageFeedAsync(new CreatePackageFeedRequest
                {
                    Name = _model.Name,
                    Description = _model.Description,
                    FeedType = _model.FeedType,
                    UpstreamUrl = _model.UpstreamUrl
                }),
                "Created",
                _ => { Dialog.Close(true); return Task.CompletedTask; },
                successTitleKey: "Created");
        }
        finally { _busy = false; }
    }

    private void Cancel() => Dialog.Close(false);

    private sealed class EditModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public PackageFeedType FeedType { get; set; } = PackageFeedType.NuGet;

        [Required]
        [StringLength(500)]
        public string UpstreamUrl { get; set; } = PackageRegistryDefaults.NuGet;

        [StringLength(500)]
        public string? Description { get; set; }
    }
}
