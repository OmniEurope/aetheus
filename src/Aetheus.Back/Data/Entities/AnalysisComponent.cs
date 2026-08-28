// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AnalysisComponent
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public int AnalysisReportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? PackageUrl { get; set; }
    public string? ComponentType { get; set; }
    public string? LicensesJson { get; set; }
    public string? Hash { get; set; }
    public bool IsDirect { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public AnalysisReport AnalysisReport { get; set; } = null!;
}
