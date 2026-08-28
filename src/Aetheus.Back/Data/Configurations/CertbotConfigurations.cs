// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class CertbotCertificateConfiguration : IEntityTypeConfiguration<CertbotCertificate>
{
    public void Configure(EntityTypeBuilder<CertbotCertificate> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.CertbotCertificates)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
