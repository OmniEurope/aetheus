// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class VariableLibraryConfiguration : IEntityTypeConfiguration<VariableLibrary>
{
    public void Configure(EntityTypeBuilder<VariableLibrary> builder)
    {
        // Per-owner name uniqueness via partial unique indexes (exactly one owner FK set).
        builder.HasIndex(e => new { e.Name, e.ProjectId }).IsUnique().HasFilter("\"ProjectId\" IS NOT NULL");
        builder.HasIndex(e => new { e.Name, e.EnvironmentId }).IsUnique().HasFilter("\"EnvironmentId\" IS NOT NULL");
        builder.HasIndex(e => new { e.Name, e.ProjectServerId }).IsUnique().HasFilter("\"ProjectServerId\" IS NOT NULL");
        builder.HasOne(e => e.Project)
               .WithMany()
               .HasForeignKey(e => e.ProjectId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Environment)
               .WithMany(env => env.Libraries)
               .HasForeignKey(e => e.EnvironmentId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.ProjectServer)
               .WithMany(ps => ps.Libraries)
               .HasForeignKey(e => e.ProjectServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VariableLibraryEntryConfiguration : IEntityTypeConfiguration<VariableLibraryEntry>
{
    public void Configure(EntityTypeBuilder<VariableLibraryEntry> builder)
    {
        builder.HasIndex(e => new { e.VariableLibraryId, e.Key }).IsUnique();
        builder.HasOne(e => e.VariableLibrary)
               .WithMany(vl => vl.Entries)
               .HasForeignKey(e => e.VariableLibraryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VariableLibraryEntryVersionConfiguration : IEntityTypeConfiguration<VariableLibraryEntryVersion>
{
    public void Configure(EntityTypeBuilder<VariableLibraryEntryVersion> builder)
    {
        builder.HasIndex(e => e.VariableLibraryEntryId);
        builder.HasOne(e => e.VariableLibraryEntry)
               .WithMany(e => e.Versions)
               .HasForeignKey(e => e.VariableLibraryEntryId)
               .HasConstraintName("FK_VarLibEntryVersions_VarLibEntries_EntryId")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
