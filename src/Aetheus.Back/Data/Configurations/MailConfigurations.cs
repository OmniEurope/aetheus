// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class MailStateConfiguration : IEntityTypeConfiguration<MailState>
{
    public void Configure(EntityTypeBuilder<MailState> builder)
    {
        builder.Property(e => e.Hostname).HasMaxLength(253);
        builder.Property(e => e.TlsCertPath).HasMaxLength(512);
        builder.Property(e => e.TlsSubject).HasMaxLength(512);
        builder.Property(e => e.TlsIssuer).HasMaxLength(512);
        builder.Property(e => e.SpamFilterName).HasMaxLength(40);
        builder.Property(e => e.SpamFilterVersion).HasMaxLength(100);
        builder.HasIndex(e => e.ServerId).IsUnique();
        builder.HasOne(e => e.Server)
               .WithOne(s => s.MailState)
               .HasForeignKey<MailState>(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MailDomainConfiguration : IEntityTypeConfiguration<MailDomain>
{
    public void Configure(EntityTypeBuilder<MailDomain> builder)
    {
        builder.Property(e => e.DkimPublicKey).HasMaxLength(4096);
        builder.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.MailDomains)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MailAccountConfiguration : IEntityTypeConfiguration<MailAccount>
{
    public void Configure(EntityTypeBuilder<MailAccount> builder)
    {
        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasOne(e => e.MailDomain)
               .WithMany(d => d.Accounts)
               .HasForeignKey(e => e.MailDomainId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MailAliasConfiguration : IEntityTypeConfiguration<MailAlias>
{
    public void Configure(EntityTypeBuilder<MailAlias> builder)
    {
        builder.HasIndex(e => e.SourceEmail);
        builder.HasOne(e => e.MailDomain)
               .WithMany(d => d.Aliases)
               .HasForeignKey(e => e.MailDomainId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
