// SPDX-License-Identifier: EUPL-1.2
// Test corpus for .aetheus/security-rules/opengrep/aetheus-security.yml, C# rule. Not compiled.
public static class SecurityRuleCorpus
{
    public static void Query(AppDbContext db, string name)
    {
        // ruleid: aetheus.csharp.raw-sql-interpolation
        db.Database.ExecuteSqlRaw($"DELETE FROM Users WHERE Name = '{name}'");
        // ok: aetheus.csharp.raw-sql-interpolation
        db.Database.ExecuteSqlRaw("DELETE FROM Users WHERE Name = {0}", name);
    }
}
