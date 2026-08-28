// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class PipelineFavorite
{
    public int UserId { get; set; }
    public int PipelineId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = default!;
    public Pipeline Pipeline { get; set; } = default!;
}
