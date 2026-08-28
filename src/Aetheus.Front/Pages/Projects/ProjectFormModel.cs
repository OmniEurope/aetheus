// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects;

internal sealed class ProjectFormModel
{
    public static List<object> BuildStatusOptions(IStringLocalizer<AppStrings> localizer) =>
    [
        new { Text = localizer["Active"].Value, Value = ProjectStatus.Active },
        new { Text = localizer["Archived"].Value, Value = ProjectStatus.Archived }
    ];

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public string RepositoryUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public string TagsRaw { get; set; } = string.Empty;

    [Range(1, 3650)]
    public int? ArtifactRetentionDays { get; set; }

    [Range(1, 3650)]
    public int? ArtifactLatestRetentionDays { get; set; }

    [StringLength(100)]
    public string ReleaseNumberingPattern { get; set; } = string.Empty;

    public static ProjectFormModel From(ProjectDetailDto project) => new()
    {
        Name = project.Name,
        Description = project.Description,
        RepositoryUrl = project.RepositoryUrl ?? string.Empty,
        DefaultBranch = project.DefaultBranch ?? string.Empty,
        Status = project.Status,
        TagsRaw = string.Join(", ", project.Tags),
        ArtifactRetentionDays = project.ArtifactRetentionDays,
        ArtifactLatestRetentionDays = project.ArtifactLatestRetentionDays,
        ReleaseNumberingPattern = project.ReleaseNumberingPattern ?? string.Empty
    };

    public CreateProjectRequest ToCreateRequest() => new()
    {
        Name = Name,
        Description = Description,
        RepositoryUrl = Nullify(RepositoryUrl),
        DefaultBranch = Nullify(DefaultBranch),
        Tags = ParseTags(),
        ArtifactRetentionDays = ArtifactRetentionDays,
        ArtifactLatestRetentionDays = ArtifactLatestRetentionDays,
        ReleaseNumberingPattern = Nullify(ReleaseNumberingPattern)
    };

    public UpdateProjectRequest ToUpdateRequest() => new()
    {
        Name = Name,
        Description = Description,
        RepositoryUrl = Nullify(RepositoryUrl),
        DefaultBranch = Nullify(DefaultBranch),
        Status = Status,
        Tags = ParseTags(),
        ArtifactRetentionDays = ArtifactRetentionDays,
        ArtifactLatestRetentionDays = ArtifactLatestRetentionDays,
        ReleaseNumberingPattern = Nullify(ReleaseNumberingPattern)
    };

    private List<string> ParseTags() => TagsRaw
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .ToList();

    private static string? Nullify(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
