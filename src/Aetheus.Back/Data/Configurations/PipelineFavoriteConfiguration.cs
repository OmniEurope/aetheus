// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class PipelineFavoriteConfiguration : IEntityTypeConfiguration<PipelineFavorite>
{
    public void Configure(EntityTypeBuilder<PipelineFavorite> builder)
    {
        builder.HasKey(favorite => new { favorite.UserId, favorite.PipelineId });
        builder.HasOne(favorite => favorite.User)
            .WithMany()
            .HasForeignKey(favorite => favorite.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(favorite => favorite.Pipeline)
            .WithMany()
            .HasForeignKey(favorite => favorite.PipelineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
