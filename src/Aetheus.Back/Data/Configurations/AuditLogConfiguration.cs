// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Username);
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.EntityType);
        builder.HasIndex(e => new { e.EntityType, e.Action, e.Timestamp });
    }
}
