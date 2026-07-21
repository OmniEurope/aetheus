using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AuditLogAppendOnly : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: creates the append-only trigger without removing schema or data.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user) THEN
                    EXECUTE format('REVOKE DELETE, UPDATE ON "AuditLogs" FROM %I', current_user);
                END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                EXECUTE format('GRANT DELETE, UPDATE ON "AuditLogs" TO %I', current_user);
            END $$;
            """);
    }
}
