// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class ServerPortReservationConfiguration : IEntityTypeConfiguration<ServerPortReservation>
{
    public void Configure(EntityTypeBuilder<ServerPortReservation> builder)
    {
        // One row per (server, port, protocol, source): a declared claim and an observed sighting of the
        // same port are two facts about it, not a duplicate, and the conflict rule reads both. The
        // protocol belongs in the key because 53/udp and 53/tcp are different ports - without it a UDP
        // observation would collide with a TCP claim and refuse a deployment it has nothing to do with.
        builder.HasIndex(e => new { e.ServerId, e.Port, e.Protocol, e.Source }).IsUnique();
        builder.HasIndex(e => new { e.ServerId, e.OwnerKey });

        builder.HasOne(e => e.Server)
               .WithMany()
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);

        // A deleted project releases its ports rather than blocking its own deletion; the row stays so
        // the label still names what used to hold the port.
        builder.HasOne(e => e.Project)
               .WithMany()
               .HasForeignKey(e => e.ProjectId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ServerPortRangeConfiguration : IEntityTypeConfiguration<ServerPortRange>
{
    public void Configure(EntityTypeBuilder<ServerPortRange> builder)
    {
        // At most one window per server: two ranges would make "the first free port" ambiguous.
        builder.HasIndex(e => e.ServerId).IsUnique();

        builder.HasOne(e => e.Server)
               .WithMany()
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
