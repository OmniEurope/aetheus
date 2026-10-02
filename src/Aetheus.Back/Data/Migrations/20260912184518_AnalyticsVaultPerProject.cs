using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsVaultPerProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: data move, no schema change (PLAN-003 2.1). Web
            // analytics had one vault per app, `aetheus-web-analytics-<appId>`; it now has one per project,
            // `<project slug>.analytics`, with one key per app (`..._PSEUDONYMIZATION_KEY_<appId>`).
            // The slug is AppWebAnalyticsConfigurationService.VaultNameFor, restated in SQL: lower-case,
            // every run of other characters one hyphen, trimmed, 90 at most, `project-<id>` when empty.
            // The legacy vaults are COPIED from, never modified or deleted: the key keeps its encrypted
            // value (the encryption is not bound to the vault or the key name), so pseudonyms do not
            // change, and a legacy vault nothing points to any more is removed by hand later. Every
            // statement is guarded, so re-running it adds nothing.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE analytics_vault_move ON COMMIT DROP AS
                SELECT a."Id" AS app_id,
                       a."ProjectId" AS project_id,
                       a."AnalyticsVaultName" AS legacy_name,
                       COALESCE(
                           NULLIF(btrim(left(btrim(regexp_replace(lower(p."Name"), '[^a-z0-9]+', '-', 'g'), '-'), 90), '-'), ''),
                           'project-' || p."Id") || '.analytics' AS vault_name
                FROM "MonitoredApps" AS a
                JOIN "Projects" AS p ON p."Id" = a."ProjectId"
                WHERE a."AnalyticsVaultName" IS NOT NULL
                  AND a."AnalyticsVaultName" NOT LIKE '%.analytics';

                INSERT INTO "Vaults" ("Name", "Description", "ProjectId", "CreatedAt", "UpdatedAt", "RowVersion")
                SELECT m.vault_name,
                       'Aetheus-managed pseudonymization keys for no-banner web analytics, one per app.',
                       m.project_id, now(), now(), gen_random_uuid()
                FROM (SELECT DISTINCT vault_name, project_id FROM analytics_vault_move) AS m
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Vaults" AS v
                    WHERE v."ProjectId" = m.project_id AND v."Name" = m.vault_name);

                INSERT INTO "VaultSecrets" ("VaultId", "Key", "EncryptedValue", "CreatedAt", "UpdatedAt", "ExpiresAt")
                SELECT target."Id",
                       'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY_' || m.app_id,
                       s."EncryptedValue", now(), now(), s."ExpiresAt"
                FROM analytics_vault_move AS m
                JOIN "Vaults" AS legacy ON legacy."ProjectId" = m.project_id AND legacy."Name" = m.legacy_name
                JOIN "VaultSecrets" AS s ON s."VaultId" = legacy."Id"
                    AND s."Key" = 'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY'
                JOIN "Vaults" AS target ON target."ProjectId" = m.project_id AND target."Name" = m.vault_name
                WHERE NOT EXISTS (
                    SELECT 1 FROM "VaultSecrets" AS existing
                    WHERE existing."VaultId" = target."Id"
                      AND existing."Key" = 'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY_' || m.app_id);

                -- Expand/contract: the colour still serving during the cutover, and the previous release
                -- after a return to it, read the unsuffixed key from whatever vault AnalyticsVaultName
                -- names. A project with ONE analytics app therefore also gets the unsuffixed key, same
                -- value, so that release keeps working; with several apps the name would be ambiguous and
                -- the previous release loses public ingest until the next deployment. The contract
                -- release removes this copy.
                INSERT INTO "VaultSecrets" ("VaultId", "Key", "EncryptedValue", "CreatedAt", "UpdatedAt", "ExpiresAt")
                SELECT target."Id", 'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY', own."EncryptedValue", now(), now(), own."ExpiresAt"
                FROM (SELECT project_id, vault_name, min(app_id) AS app_id
                      FROM analytics_vault_move GROUP BY project_id, vault_name HAVING count(*) = 1) AS single
                JOIN "Vaults" AS target ON target."ProjectId" = single.project_id AND target."Name" = single.vault_name
                JOIN "VaultSecrets" AS own ON own."VaultId" = target."Id"
                    AND own."Key" = 'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY_' || single.app_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM "VaultSecrets" AS existing
                    WHERE existing."VaultId" = target."Id" AND existing."Key" = 'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY');

                INSERT INTO "VaultSecretVersions" ("VaultSecretId", "Key", "EncryptedValue", "Version", "ChangedAt", "ChangeType")
                SELECT s."Id", s."Key", s."EncryptedValue", 1, now(), 0
                FROM "VaultSecrets" AS s
                JOIN "Vaults" AS target ON target."Id" = s."VaultId"
                JOIN (SELECT DISTINCT project_id, vault_name FROM analytics_vault_move) AS m
                    ON target."ProjectId" = m.project_id AND target."Name" = m.vault_name
                WHERE NOT EXISTS (SELECT 1 FROM "VaultSecretVersions" AS v WHERE v."VaultSecretId" = s."Id");

                -- Repointed only once its key is really in the project vault: an app whose legacy vault
                -- lost its key keeps its old name and stays exactly as broken as it was, visibly.
                UPDATE "MonitoredApps" AS a
                SET "AnalyticsVaultName" = m.vault_name
                FROM analytics_vault_move AS m
                WHERE a."Id" = m.app_id
                  AND EXISTS (
                      SELECT 1 FROM "VaultSecrets" AS s
                      JOIN "Vaults" AS target ON target."Id" = s."VaultId"
                      WHERE target."ProjectId" = m.project_id AND target."Name" = m.vault_name
                        AND s."Key" = 'AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY_' || m.app_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Points each app back at its legacy vault when that vault still exists; the project vaults
            // and their copied keys are left for a person to remove, since other apps may use them.
            migrationBuilder.Sql(
                """
                UPDATE "MonitoredApps" AS a
                SET "AnalyticsVaultName" = 'aetheus-web-analytics-' || a."Id"
                WHERE a."AnalyticsVaultName" LIKE '%.analytics'
                  AND EXISTS (
                      SELECT 1 FROM "Vaults" AS v
                      WHERE v."ProjectId" = a."ProjectId" AND v."Name" = 'aetheus-web-analytics-' || a."Id");
                """);
        }
    }
}
