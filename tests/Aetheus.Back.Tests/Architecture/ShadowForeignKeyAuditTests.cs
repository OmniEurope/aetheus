// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Architectural guard: fails whenever EF Core invents a duplicate (shadow) foreign-key column
/// because a navigation was left unbound. This is the exact regression behind the orphan
/// <c>PipelineArtifact.PipelineRunId1</c> column - it appeared when <c>HasOne(e =&gt; e.PipelineRun)
/// .WithMany()</c> didn't bind the inverse <c>PipelineRun.Artifacts</c> navigation, so EF mapped the
/// navigation as a second relationship and synthesised a shadow FK.
/// <para>
/// When this fails, bind the inverse navigation explicitly - e.g. <c>.WithMany(r =&gt; r.Artifacts)</c>
/// - so the two ends collapse onto the single intended FK, then add a migration to drop the column.
/// </para>
/// </summary>
public class ShadowForeignKeyAuditTests
{
    [Fact]
    public void Model_HasNoConventionDuplicatedShadowForeignKeys()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=_shadowfk_check_;Username=_;Password=_")
            .Options;
        using var context = new AppDbContext(options);

        var offenders = new List<string>();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            foreach (var fk in entity.GetForeignKeys())
            {
                foreach (var prop in fk.Properties)
                {
                    // EF appends a digit ("...Id1") only when disambiguating a second relationship to
                    // the same principal - i.e. the unbound-navigation smell. A legitimately-mapped FK
                    // is a declared CLR property (not shadow); implicit M2M join FKs end in a letter.
                    if (prop.IsShadowProperty() && char.IsDigit(prop.Name[^1]))
                        offenders.Add($"{entity.ClrType.Name}.{prop.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Convention-duplicated shadow FK column(s) detected - a navigation is mapped as a second " +
            "relationship instead of the intended one. Bind the inverse navigation explicitly " +
            "(e.g. `.WithMany(x => x.Items)`). Offenders: " + string.Join(", ", offenders));
    }
}
