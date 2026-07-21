// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class MailStateConfiguration : IEntityTypeConfiguration<MailState>
{
    public void Configure(EntityTypeBuilder<MailState> builder)
    {
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
