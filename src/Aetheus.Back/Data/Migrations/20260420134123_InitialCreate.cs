using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AgentPools",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                MaxConcurrency = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentPools", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AppSettings",
            columns: table => new
            {
                Key = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppSettings", x => x.Key);
            });

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Username = table.Column<string>(type: "text", nullable: false),
                Action = table.Column<string>(type: "text", nullable: false),
                EntityType = table.Column<string>(type: "text", nullable: false),
                EntityId = table.Column<int>(type: "integer", nullable: true),
                Details = table.Column<string>(type: "text", nullable: true),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "NotificationChannels",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                ConfigurationJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationChannels", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PipelineTemplates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "text", nullable: false),
                YamlContent = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                Changelog = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineTemplates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PluginRegistrations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                EntryPoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ConfigurationJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PluginRegistrations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Projects",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                RepositoryUrl = table.Column<string>(type: "text", nullable: true),
                DefaultBranch = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                Tags = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Projects", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Roles",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Roles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Secrets",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Key = table.Column<string>(type: "text", nullable: false),
                EncryptedValue = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Secrets", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Servers",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Hostname = table.Column<string>(type: "text", nullable: false),
                OsDescription = table.Column<string>(type: "text", nullable: false),
                IpAddress = table.Column<string>(type: "text", nullable: false),
                AgentVersion = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Tags = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Servers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Username = table.Column<string>(type: "text", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: false),
                Email = table.Column<string>(type: "text", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WebhookSubscriptions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TargetUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FailureCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "NotificationRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                NotificationChannelId = table.Column<int>(type: "integer", nullable: false),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FilterJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_NotificationRules_NotificationChannels_NotificationChannelId",
                    column: x => x.NotificationChannelId,
                    principalTable: "NotificationChannels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Environments",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                RequireApproval = table.Column<bool>(type: "boolean", nullable: false),
                ApprovalTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                ApprovalInstructions = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Environments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Environments_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "Pipelines",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                YamlDefinition = table.Column<string>(type: "text", nullable: false),
                TriggerType = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Pipelines", x => x.Id);
                table.ForeignKey(
                    name: "FK_Pipelines_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "ServiceConnections",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                EncryptedPayload = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceConnections", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServiceConnections_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "VariableLibraries",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariableLibraries", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariableLibraries_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Vaults",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vaults", x => x.Id);
                table.ForeignKey(
                    name: "FK_Vaults_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ResourcePermissions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RoleId = table.Column<int>(type: "integer", nullable: false),
                ResourceType = table.Column<int>(type: "integer", nullable: false),
                ResourceId = table.Column<int>(type: "integer", nullable: true),
                Permission = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ResourcePermissions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ResourcePermissions_Roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AgentPoolServers",
            columns: table => new
            {
                AgentPoolId = table.Column<int>(type: "integer", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentPoolServers", x => new { x.AgentPoolId, x.ServerId });
                table.ForeignKey(
                    name: "FK_AgentPoolServers_AgentPools_AgentPoolId",
                    column: x => x.AgentPoolId,
                    principalTable: "AgentPools",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AgentPoolServers_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AlertRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: true),
                Metric = table.Column<int>(type: "integer", nullable: false),
                Operator = table.Column<int>(type: "integer", nullable: false),
                Threshold = table.Column<double>(type: "double precision", nullable: false),
                SustainedSeconds = table.Column<int>(type: "integer", nullable: false),
                Severity = table.Column<int>(type: "integer", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                NotificationChannelId = table.Column<int>(type: "integer", nullable: true),
                LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlertRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_AlertRules_NotificationChannels_NotificationChannelId",
                    column: x => x.NotificationChannelId,
                    principalTable: "NotificationChannels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AlertRules_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApacheModules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Type = table.Column<string>(type: "text", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApacheModules", x => x.Id);
                table.ForeignKey(
                    name: "FK_ApacheModules_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApacheStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "text", nullable: false),
                IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                Pid = table.Column<int>(type: "integer", nullable: true),
                ConfigRoot = table.Column<string>(type: "text", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApacheStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_ApacheStates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApacheVirtualHosts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                ServerName = table.Column<string>(type: "text", nullable: false),
                Port = table.Column<int>(type: "integer", nullable: false),
                DocumentRoot = table.Column<string>(type: "text", nullable: false),
                ConfigFile = table.Column<string>(type: "text", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApacheVirtualHosts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ApacheVirtualHosts_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CertbotCertificates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Domains = table.Column<string>(type: "text", nullable: false),
                ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CertPath = table.Column<string>(type: "text", nullable: false),
                KeyPath = table.Column<string>(type: "text", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CertbotCertificates", x => x.Id);
                table.ForeignKey(
                    name: "FK_CertbotCertificates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DockerComposeStacks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                ConfigFile = table.Column<string>(type: "text", nullable: false),
                RunningCount = table.Column<int>(type: "integer", nullable: false),
                TotalCount = table.Column<int>(type: "integer", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockerComposeStacks", x => x.Id);
                table.ForeignKey(
                    name: "FK_DockerComposeStacks_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DockerContainers",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                ContainerId = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Image = table.Column<string>(type: "text", nullable: false),
                State = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Ports = table.Column<string>(type: "text", nullable: false),
                CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                MemoryUsageMb = table.Column<double>(type: "double precision", nullable: false),
                MemoryLimitMb = table.Column<double>(type: "double precision", nullable: false),
                Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockerContainers", x => x.Id);
                table.ForeignKey(
                    name: "FK_DockerContainers_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DockerImages",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                ImageId = table.Column<string>(type: "text", nullable: false),
                Repository = table.Column<string>(type: "text", nullable: false),
                Tag = table.Column<string>(type: "text", nullable: false),
                Size = table.Column<string>(type: "text", nullable: false),
                Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockerImages", x => x.Id);
                table.ForeignKey(
                    name: "FK_DockerImages_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DockerNetworks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                NetworkId = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Driver = table.Column<string>(type: "text", nullable: false),
                Scope = table.Column<string>(type: "text", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockerNetworks", x => x.Id);
                table.ForeignKey(
                    name: "FK_DockerNetworks_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DockerVolumes",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Driver = table.Column<string>(type: "text", nullable: false),
                Mountpoint = table.Column<string>(type: "text", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockerVolumes", x => x.Id);
                table.ForeignKey(
                    name: "FK_DockerVolumes_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MailDomains",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                DkimSelector = table.Column<string>(type: "text", nullable: false),
                HasSpf = table.Column<bool>(type: "boolean", nullable: false),
                HasDkim = table.Column<bool>(type: "boolean", nullable: false),
                HasDmarc = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MailDomains", x => x.Id);
                table.ForeignKey(
                    name: "FK_MailDomains_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MailStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                PostfixVersion = table.Column<string>(type: "text", nullable: false),
                DovecotVersion = table.Column<string>(type: "text", nullable: false),
                IsPostfixRunning = table.Column<bool>(type: "boolean", nullable: false),
                IsDovecotRunning = table.Column<bool>(type: "boolean", nullable: false),
                QueueSize = table.Column<int>(type: "integer", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MailStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_MailStates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ModuleLinks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                SourceType = table.Column<int>(type: "integer", nullable: false),
                SourceIdentifier = table.Column<string>(type: "text", nullable: false),
                TargetType = table.Column<int>(type: "integer", nullable: false),
                TargetIdentifier = table.Column<string>(type: "text", nullable: false),
                IsAutoDetected = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ModuleLinks", x => x.Id);
                table.ForeignKey(
                    name: "FK_ModuleLinks_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PortsentryBlockedIps",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                IpAddress = table.Column<string>(type: "text", nullable: false),
                Protocol = table.Column<string>(type: "text", nullable: false),
                BlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PortsentryBlockedIps", x => x.Id);
                table.ForeignKey(
                    name: "FK_PortsentryBlockedIps_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PortsentryStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<string>(type: "text", nullable: false),
                Mode = table.Column<string>(type: "text", nullable: false),
                TcpPorts = table.Column<string>(type: "text", nullable: false),
                UdpPorts = table.Column<string>(type: "text", nullable: false),
                BlockedCount = table.Column<int>(type: "integer", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PortsentryStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_PortsentryStates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PortsentryWhitelistIps",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                IpAddress = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PortsentryWhitelistIps", x => x.Id);
                table.ForeignKey(
                    name: "FK_PortsentryWhitelistIps_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RegistrationTokens",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Token = table.Column<string>(type: "text", nullable: false),
                IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                UsedByServerId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RegistrationTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_RegistrationTokens_Servers_UsedByServerId",
                    column: x => x.UsedByServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "RkhunterScanResults",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                ScanTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                Summary = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RkhunterScanResults", x => x.Id);
                table.ForeignKey(
                    name: "FK_RkhunterScanResults_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RkhunterStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "text", nullable: false),
                DatabaseVersion = table.Column<string>(type: "text", nullable: false),
                LastScanTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastScanStatus = table.Column<string>(type: "text", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                DatabaseLastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ScanScheduleCron = table.Column<string>(type: "text", nullable: true),
                LastScheduledScanAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RkhunterStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_RkhunterStates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RkhunterWarnings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Category = table.Column<string>(type: "text", nullable: false),
                Detail = table.Column<string>(type: "text", nullable: false),
                Severity = table.Column<string>(type: "text", nullable: false),
                FoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsArchived = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RkhunterWarnings", x => x.Id);
                table.ForeignKey(
                    name: "FK_RkhunterWarnings_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ServerApps",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<string>(type: "text", nullable: true),
                Type = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                Port = table.Column<int>(type: "integer", nullable: true),
                Path = table.Column<string>(type: "text", nullable: true),
                Source = table.Column<string>(type: "text", nullable: false),
                InstalledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServerApps", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServerApps_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ServerMetrics",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                MemoryUsedMb = table.Column<double>(type: "double precision", nullable: false),
                MemoryTotalMb = table.Column<double>(type: "double precision", nullable: false),
                DiskUsedGb = table.Column<double>(type: "double precision", nullable: false),
                DiskTotalGb = table.Column<double>(type: "double precision", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServerMetrics", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServerMetrics_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ServerModules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "text", nullable: true),
                Configuration = table.Column<string>(type: "text", nullable: false),
                InstalledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServerModules", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServerModules_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ServerTokens",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                TokenHash = table.Column<string>(type: "text", nullable: false),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServerTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServerTokens_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ServiceInfos",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceInfos", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServiceInfos_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TeamspeakChannels",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                ChannelId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                ParentId = table.Column<int>(type: "integer", nullable: false),
                Order = table.Column<int>(type: "integer", nullable: false),
                TotalClients = table.Column<int>(type: "integer", nullable: false),
                MaxClients = table.Column<int>(type: "integer", nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                HasPassword = table.Column<bool>(type: "boolean", nullable: false),
                IsPermanent = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamspeakChannels", x => x.Id);
                table.ForeignKey(
                    name: "FK_TeamspeakChannels_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TeamspeakStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "text", nullable: false),
                IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                ServerName = table.Column<string>(type: "text", nullable: false),
                VoicePort = table.Column<int>(type: "integer", nullable: false),
                QueryPort = table.Column<int>(type: "integer", nullable: false),
                MaxClients = table.Column<int>(type: "integer", nullable: false),
                OnlineClients = table.Column<int>(type: "integer", nullable: false),
                ChannelCount = table.Column<int>(type: "integer", nullable: false),
                UptimeSeconds = table.Column<long>(type: "bigint", nullable: false),
                InstallPath = table.Column<string>(type: "text", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamspeakStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_TeamspeakStates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Dashboards",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Dashboards", x => x.Id);
                table.ForeignKey(
                    name: "FK_Dashboards_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ExternalLogins",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<int>(type: "integer", nullable: false),
                Provider = table.Column<string>(type: "text", nullable: false),
                ProviderSubjectId = table.Column<string>(type: "text", nullable: false),
                DisplayName = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalLogins", x => x.Id);
                table.ForeignKey(
                    name: "FK_ExternalLogins_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            columns: table => new
            {
                UserId = table.Column<int>(type: "integer", nullable: false),
                RoleId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EnvironmentChecks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Configuration = table.Column<string>(type: "text", nullable: true),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EnvironmentChecks", x => x.Id);
                table.ForeignKey(
                    name: "FK_EnvironmentChecks_Environments_EnvironmentId",
                    column: x => x.EnvironmentId,
                    principalTable: "Environments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EnvironmentServers",
            columns: table => new
            {
                EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EnvironmentServers", x => new { x.EnvironmentId, x.ServerId });
                table.ForeignKey(
                    name: "FK_EnvironmentServers_Environments_EnvironmentId",
                    column: x => x.EnvironmentId,
                    principalTable: "Environments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_EnvironmentServers_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PipelineRuns",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineId = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AdditionalVariablesJson = table.Column<string>(type: "text", nullable: false),
                ResolvedVariablesJson = table.Column<string>(type: "text", nullable: true),
                WarningsJson = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_PipelineRuns_Pipelines_PipelineId",
                    column: x => x.PipelineId,
                    principalTable: "Pipelines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TestSuites",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                PipelineId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TestSuites", x => x.Id);
                table.ForeignKey(
                    name: "FK_TestSuites_Pipelines_PipelineId",
                    column: x => x.PipelineId,
                    principalTable: "Pipelines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_TestSuites_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GitConnections",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                ProviderType = table.Column<int>(type: "integer", nullable: false),
                OwnerOrGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                RepositoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ServiceConnectionId = table.Column<int>(type: "integer", nullable: true),
                WebhookSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                AutoSyncEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GitConnections", x => x.Id);
                table.ForeignKey(
                    name: "FK_GitConnections_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_GitConnections_ServiceConnections_ServiceConnectionId",
                    column: x => x.ServiceConnectionId,
                    principalTable: "ServiceConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "PackageFeeds",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                FeedType = table.Column<int>(type: "integer", nullable: false),
                UpstreamUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                ServiceConnectionId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackageFeeds", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackageFeeds_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_PackageFeeds_ServiceConnections_ServiceConnectionId",
                    column: x => x.ServiceConnectionId,
                    principalTable: "ServiceConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "VariableLibraryEntries",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                VariableLibraryId = table.Column<int>(type: "integer", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariableLibraryEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariableLibraryEntries_VariableLibraries_VariableLibraryId",
                    column: x => x.VariableLibraryId,
                    principalTable: "VariableLibraries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VaultSecrets",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                VaultId = table.Column<int>(type: "integer", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                EncryptedValue = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VaultSecrets", x => x.Id);
                table.ForeignKey(
                    name: "FK_VaultSecrets_Vaults_VaultId",
                    column: x => x.VaultId,
                    principalTable: "Vaults",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MailAccounts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MailDomainId = table.Column<int>(type: "integer", nullable: false),
                Email = table.Column<string>(type: "text", nullable: false),
                QuotaMb = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MailAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_MailAccounts_MailDomains_MailDomainId",
                    column: x => x.MailDomainId,
                    principalTable: "MailDomains",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MailAliases",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MailDomainId = table.Column<int>(type: "integer", nullable: false),
                SourceEmail = table.Column<string>(type: "text", nullable: false),
                DestinationEmail = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MailAliases", x => x.Id);
                table.ForeignKey(
                    name: "FK_MailAliases_MailDomains_MailDomainId",
                    column: x => x.MailDomainId,
                    principalTable: "MailDomains",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DashboardWidgets",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DashboardId = table.Column<int>(type: "integer", nullable: false),
                WidgetType = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Column = table.Column<int>(type: "integer", nullable: false),
                Row = table.Column<int>(type: "integer", nullable: false),
                Width = table.Column<int>(type: "integer", nullable: false),
                Height = table.Column<int>(type: "integer", nullable: false),
                ConfigurationJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsVisible = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                table.ForeignKey(
                    name: "FK_DashboardWidgets_Dashboards_DashboardId",
                    column: x => x.DashboardId,
                    principalTable: "Dashboards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PipelineApprovals",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                StageName = table.Column<string>(type: "text", nullable: false),
                EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResolvedByUserId = table.Column<int>(type: "integer", nullable: true),
                Comments = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineApprovals", x => x.Id);
                table.ForeignKey(
                    name: "FK_PipelineApprovals_Environments_EnvironmentId",
                    column: x => x.EnvironmentId,
                    principalTable: "Environments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PipelineApprovals_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PipelineApprovals_Users_ResolvedByUserId",
                    column: x => x.ResolvedByUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "PipelineArtifacts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                FilePath = table.Column<string>(type: "text", nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                StageName = table.Column<string>(type: "text", nullable: true),
                StepName = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineArtifacts", x => x.Id);
                table.ForeignKey(
                    name: "FK_PipelineArtifacts_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Releases",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "text", nullable: false),
                BranchName = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PromotedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RolledBackAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PipelineRunId = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Releases", x => x.Id);
                table.ForeignKey(
                    name: "FK_Releases_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Releases_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TestResults",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                StageName = table.Column<string>(type: "text", nullable: true),
                StepName = table.Column<string>(type: "text", nullable: true),
                TestName = table.Column<string>(type: "text", nullable: false),
                TestSuite = table.Column<string>(type: "text", nullable: true),
                Outcome = table.Column<int>(type: "integer", nullable: false),
                DurationMs = table.Column<double>(type: "double precision", nullable: false),
                ErrorMessage = table.Column<string>(type: "text", nullable: true),
                StackTrace = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TestResults", x => x.Id);
                table.ForeignKey(
                    name: "FK_TestResults_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                AssigneeUserId = table.Column<int>(type: "integer", nullable: true),
                ParentId = table.Column<int>(type: "integer", nullable: true),
                Priority = table.Column<int>(type: "integer", nullable: false),
                Order = table.Column<int>(type: "integer", nullable: false),
                Tags = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                LinkedPipelineRunId = table.Column<int>(type: "integer", nullable: true),
                ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkItems_PipelineRuns_LinkedPipelineRunId",
                    column: x => x.LinkedPipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_WorkItems_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_WorkItems_Users_AssigneeUserId",
                    column: x => x.AssigneeUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_WorkItems_WorkItems_ParentId",
                    column: x => x.ParentId,
                    principalTable: "WorkItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TestCases",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TestSuiteId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                AutomatedTestClass = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                AutomatedTestMethod = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                LastOutcome = table.Column<int>(type: "integer", nullable: true),
                LastPipelineRunId = table.Column<int>(type: "integer", nullable: true),
                LastDurationMs = table.Column<double>(type: "double precision", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TestCases", x => x.Id);
                table.ForeignKey(
                    name: "FK_TestCases_PipelineRuns_LastPipelineRunId",
                    column: x => x.LastPipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_TestCases_TestSuites_TestSuiteId",
                    column: x => x.TestSuiteId,
                    principalTable: "TestSuites",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BranchPolicies",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GitConnectionId = table.Column<int>(type: "integer", nullable: false),
                BranchPattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                PolicyType = table.Column<int>(type: "integer", nullable: false),
                ConfigurationJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchPolicies", x => x.Id);
                table.ForeignKey(
                    name: "FK_BranchPolicies_GitConnections_GitConnectionId",
                    column: x => x.GitConnectionId,
                    principalTable: "GitConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GitInternalRepos",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                DefaultBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IsEmpty = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastPushAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                GitConnectionId = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GitInternalRepos", x => x.Id);
                table.ForeignKey(
                    name: "FK_GitInternalRepos_GitConnections_GitConnectionId",
                    column: x => x.GitConnectionId,
                    principalTable: "GitConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_GitInternalRepos_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PullRequests",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GitConnectionId = table.Column<int>(type: "integer", nullable: false),
                ExternalId = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                SourceBranch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                TargetBranch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                AuthorLogin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                HeadCommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                MergeCommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                LinkedPipelineRunId = table.Column<int>(type: "integer", nullable: true),
                ExternalCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExternalMergedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PullRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_PullRequests_GitConnections_GitConnectionId",
                    column: x => x.GitConnectionId,
                    principalTable: "GitConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PullRequests_PipelineRuns_LinkedPipelineRunId",
                    column: x => x.LinkedPipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "PackageEntries",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PackageFeedId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                LatestVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackageEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackageEntries_PackageFeeds_PackageFeedId",
                    column: x => x.PackageFeedId,
                    principalTable: "PackageFeeds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VariableLibraryEntryVersions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                VariableLibraryEntryId = table.Column<int>(type: "integer", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ChangeType = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariableLibraryEntryVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_VarLibEntryVersions_VarLibEntries_EntryId",
                    column: x => x.VariableLibraryEntryId,
                    principalTable: "VariableLibraryEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VaultSecretVersions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                VaultSecretId = table.Column<int>(type: "integer", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                EncryptedValue = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ChangeType = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VaultSecretVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_VaultSecretVersions_VaultSecrets_VaultSecretId",
                    column: x => x.VaultSecretId,
                    principalTable: "VaultSecrets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BranchProtectionRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GitInternalRepoId = table.Column<int>(type: "integer", nullable: false),
                Pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PreventDeletion = table.Column<bool>(type: "boolean", nullable: false),
                PreventForcePush = table.Column<bool>(type: "boolean", nullable: false),
                RequirePullRequest = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchProtectionRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_BranchProtectionRules_GitInternalRepos_GitInternalRepoId",
                    column: x => x.GitInternalRepoId,
                    principalTable: "GitInternalRepos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PipelineStepRuns",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                StageName = table.Column<string>(type: "text", nullable: false),
                StepName = table.Column<string>(type: "text", nullable: false),
                Order = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: true),
                TaskId = table.Column<int>(type: "integer", nullable: true),
                ExitCode = table.Column<int>(type: "integer", nullable: true),
                OutputVariablesJson = table.Column<string>(type: "text", nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                ContinueOnError = table.Column<bool>(type: "boolean", nullable: false),
                MatrixLeg = table.Column<string>(type: "text", nullable: true),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineStepRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_PipelineStepRuns_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PipelineStepRuns_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "Tasks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Command = table.Column<string>(type: "text", nullable: false),
                Executor = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                PipelineRunId = table.Column<int>(type: "integer", nullable: true),
                PipelineStepRunId = table.Column<int>(type: "integer", nullable: true),
                EnvironmentVariables = table.Column<string>(type: "text", nullable: false),
                TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                ExitCode = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tasks", x => x.Id);
                table.ForeignKey(
                    name: "FK_Tasks_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Tasks_PipelineStepRuns_PipelineStepRunId",
                    column: x => x.PipelineStepRunId,
                    principalTable: "PipelineStepRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Tasks_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TaskLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TaskId = table.Column<int>(type: "integer", nullable: false),
                Level = table.Column<int>(type: "integer", nullable: false),
                Message = table.Column<string>(type: "text", nullable: false),
                OriginalMessage = table.Column<string>(type: "text", nullable: true),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaskLogs_Tasks_TaskId",
                    column: x => x.TaskId,
                    principalTable: "Tasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AgentPools_Name",
            table: "AgentPools",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AgentPoolServers_ServerId",
            table: "AgentPoolServers",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_AlertRules_Name",
            table: "AlertRules",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AlertRules_NotificationChannelId",
            table: "AlertRules",
            column: "NotificationChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_AlertRules_ServerId",
            table: "AlertRules",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_ApacheModules_ServerId",
            table: "ApacheModules",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_ApacheStates_ServerId",
            table: "ApacheStates",
            column: "ServerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ApacheVirtualHosts_ServerId",
            table: "ApacheVirtualHosts",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_Timestamp",
            table: "AuditLogs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_Username",
            table: "AuditLogs",
            column: "Username");

        migrationBuilder.CreateIndex(
            name: "IX_BranchPolicies_GitConnectionId",
            table: "BranchPolicies",
            column: "GitConnectionId");

        migrationBuilder.CreateIndex(
            name: "IX_BranchProtectionRules_GitInternalRepoId_Pattern",
            table: "BranchProtectionRules",
            columns: new[] { "GitInternalRepoId", "Pattern" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CertbotCertificates_ServerId_Name",
            table: "CertbotCertificates",
            columns: new[] { "ServerId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Dashboards_UserId_Name",
            table: "Dashboards",
            columns: new[] { "UserId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DashboardWidgets_DashboardId",
            table: "DashboardWidgets",
            column: "DashboardId");

        migrationBuilder.CreateIndex(
            name: "IX_DockerComposeStacks_ServerId_Name",
            table: "DockerComposeStacks",
            columns: new[] { "ServerId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DockerContainers_ServerId_ContainerId",
            table: "DockerContainers",
            columns: new[] { "ServerId", "ContainerId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DockerImages_ServerId_ImageId",
            table: "DockerImages",
            columns: new[] { "ServerId", "ImageId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DockerNetworks_ServerId_NetworkId",
            table: "DockerNetworks",
            columns: new[] { "ServerId", "NetworkId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DockerVolumes_ServerId_Name",
            table: "DockerVolumes",
            columns: new[] { "ServerId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EnvironmentChecks_EnvironmentId",
            table: "EnvironmentChecks",
            column: "EnvironmentId");

        migrationBuilder.CreateIndex(
            name: "IX_Environments_Name",
            table: "Environments",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Environments_ProjectId",
            table: "Environments",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_EnvironmentServers_ServerId",
            table: "EnvironmentServers",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_ExternalLogins_Provider_ProviderSubjectId",
            table: "ExternalLogins",
            columns: new[] { "Provider", "ProviderSubjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExternalLogins_UserId",
            table: "ExternalLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_GitConnections_ProjectId_ProviderType_OwnerOrGroup_Reposito~",
            table: "GitConnections",
            columns: new[] { "ProjectId", "ProviderType", "OwnerOrGroup", "RepositoryName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_GitConnections_ServiceConnectionId",
            table: "GitConnections",
            column: "ServiceConnectionId");

        migrationBuilder.CreateIndex(
            name: "IX_GitInternalRepos_GitConnectionId",
            table: "GitInternalRepos",
            column: "GitConnectionId");

        migrationBuilder.CreateIndex(
            name: "IX_GitInternalRepos_ProjectId_Slug",
            table: "GitInternalRepos",
            columns: new[] { "ProjectId", "Slug" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MailAccounts_Email",
            table: "MailAccounts",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MailAccounts_MailDomainId",
            table: "MailAccounts",
            column: "MailDomainId");

        migrationBuilder.CreateIndex(
            name: "IX_MailAliases_MailDomainId",
            table: "MailAliases",
            column: "MailDomainId");

        migrationBuilder.CreateIndex(
            name: "IX_MailAliases_SourceEmail",
            table: "MailAliases",
            column: "SourceEmail");

        migrationBuilder.CreateIndex(
            name: "IX_MailDomains_ServerId_Name",
            table: "MailDomains",
            columns: new[] { "ServerId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MailStates_ServerId",
            table: "MailStates",
            column: "ServerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ModuleLinks_ServerId_SourceType_SourceIdentifier_TargetType~",
            table: "ModuleLinks",
            columns: new[] { "ServerId", "SourceType", "SourceIdentifier", "TargetType", "TargetIdentifier" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_NotificationChannels_Name",
            table: "NotificationChannels",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_NotificationRules_NotificationChannelId",
            table: "NotificationRules",
            column: "NotificationChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_PackageEntries_PackageFeedId_Name",
            table: "PackageEntries",
            columns: new[] { "PackageFeedId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PackageFeeds_Name",
            table: "PackageFeeds",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PackageFeeds_ProjectId",
            table: "PackageFeeds",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_PackageFeeds_ServiceConnectionId",
            table: "PackageFeeds",
            column: "ServiceConnectionId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineApprovals_EnvironmentId",
            table: "PipelineApprovals",
            column: "EnvironmentId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineApprovals_PipelineRunId_StageName",
            table: "PipelineApprovals",
            columns: new[] { "PipelineRunId", "StageName" });

        migrationBuilder.CreateIndex(
            name: "IX_PipelineApprovals_ResolvedByUserId",
            table: "PipelineApprovals",
            column: "ResolvedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_PipelineRunId",
            table: "PipelineArtifacts",
            column: "PipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineRuns_PipelineId",
            table: "PipelineRuns",
            column: "PipelineId");

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_Name",
            table: "Pipelines",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_ProjectId",
            table: "Pipelines",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineStepRuns_PipelineRunId",
            table: "PipelineStepRuns",
            column: "PipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineStepRuns_ServerId",
            table: "PipelineStepRuns",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineStepRuns_TaskId",
            table: "PipelineStepRuns",
            column: "TaskId");

        migrationBuilder.CreateIndex(
            name: "IX_PluginRegistrations_Name_Version",
            table: "PluginRegistrations",
            columns: new[] { "Name", "Version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PortsentryBlockedIps_ServerId_IpAddress",
            table: "PortsentryBlockedIps",
            columns: new[] { "ServerId", "IpAddress" });

        migrationBuilder.CreateIndex(
            name: "IX_PortsentryStates_ServerId",
            table: "PortsentryStates",
            column: "ServerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PortsentryWhitelistIps_ServerId",
            table: "PortsentryWhitelistIps",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_Projects_Name",
            table: "Projects",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PullRequests_GitConnectionId_ExternalId",
            table: "PullRequests",
            columns: new[] { "GitConnectionId", "ExternalId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PullRequests_LinkedPipelineRunId",
            table: "PullRequests",
            column: "LinkedPipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_RegistrationTokens_Token",
            table: "RegistrationTokens",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RegistrationTokens_UsedByServerId",
            table: "RegistrationTokens",
            column: "UsedByServerId");

        migrationBuilder.CreateIndex(
            name: "IX_Releases_PipelineRunId",
            table: "Releases",
            column: "PipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_Releases_ProjectId_Version",
            table: "Releases",
            columns: new[] { "ProjectId", "Version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ResourcePermissions_RoleId_ResourceType_ResourceId_Permissi~",
            table: "ResourcePermissions",
            columns: new[] { "RoleId", "ResourceType", "ResourceId", "Permission" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RkhunterScanResults_ServerId",
            table: "RkhunterScanResults",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_RkhunterStates_ServerId",
            table: "RkhunterStates",
            column: "ServerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RkhunterWarnings_ServerId",
            table: "RkhunterWarnings",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_Roles_Name",
            table: "Roles",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Secrets_Key",
            table: "Secrets",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServerApps_ServerId",
            table: "ServerApps",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_ServerMetrics_ServerId_Timestamp",
            table: "ServerMetrics",
            columns: new[] { "ServerId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_ServerModules_ServerId",
            table: "ServerModules",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_Servers_Hostname",
            table: "Servers",
            column: "Hostname");

        migrationBuilder.CreateIndex(
            name: "IX_Servers_Name",
            table: "Servers",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServerTokens_ServerId",
            table: "ServerTokens",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_ServerTokens_TokenHash",
            table: "ServerTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServiceConnections_Name",
            table: "ServiceConnections",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServiceConnections_ProjectId",
            table: "ServiceConnections",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ServiceInfos_ServerId_Name",
            table: "ServiceInfos",
            columns: new[] { "ServerId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TaskLogs_TaskId",
            table: "TaskLogs",
            column: "TaskId");

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_PipelineRunId",
            table: "Tasks",
            column: "PipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_PipelineStepRunId",
            table: "Tasks",
            column: "PipelineStepRunId");

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_ServerId_Status",
            table: "Tasks",
            columns: new[] { "ServerId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_TeamspeakChannels_ServerId_ChannelId",
            table: "TeamspeakChannels",
            columns: new[] { "ServerId", "ChannelId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TeamspeakStates_ServerId",
            table: "TeamspeakStates",
            column: "ServerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TestCases_LastPipelineRunId",
            table: "TestCases",
            column: "LastPipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_TestCases_TestSuiteId",
            table: "TestCases",
            column: "TestSuiteId");

        migrationBuilder.CreateIndex(
            name: "IX_TestResults_PipelineRunId",
            table: "TestResults",
            column: "PipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_TestSuites_PipelineId",
            table: "TestSuites",
            column: "PipelineId");

        migrationBuilder.CreateIndex(
            name: "IX_TestSuites_ProjectId_Name",
            table: "TestSuites",
            columns: new[] { "ProjectId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_Username",
            table: "Users",
            column: "Username",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_Name_ProjectId",
            table: "VariableLibraries",
            columns: new[] { "Name", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_ProjectId",
            table: "VariableLibraries",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraryEntries_VariableLibraryId_Key",
            table: "VariableLibraryEntries",
            columns: new[] { "VariableLibraryId", "Key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraryEntryVersions_VariableLibraryEntryId",
            table: "VariableLibraryEntryVersions",
            column: "VariableLibraryEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_Name_ProjectId",
            table: "Vaults",
            columns: new[] { "Name", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_ProjectId",
            table: "Vaults",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_VaultSecrets_VaultId_Key",
            table: "VaultSecrets",
            columns: new[] { "VaultId", "Key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VaultSecretVersions_VaultSecretId",
            table: "VaultSecretVersions",
            column: "VaultSecretId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkItems_AssigneeUserId",
            table: "WorkItems",
            column: "AssigneeUserId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkItems_LinkedPipelineRunId",
            table: "WorkItems",
            column: "LinkedPipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkItems_ParentId",
            table: "WorkItems",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkItems_ProjectId",
            table: "WorkItems",
            column: "ProjectId");

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineStepRuns_Tasks_TaskId",
            table: "PipelineStepRuns",
            column: "TaskId",
            principalTable: "Tasks",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PipelineStepRuns_Servers_ServerId",
            table: "PipelineStepRuns");

        migrationBuilder.DropForeignKey(
            name: "FK_Tasks_Servers_ServerId",
            table: "Tasks");

        migrationBuilder.DropForeignKey(
            name: "FK_Pipelines_Projects_ProjectId",
            table: "Pipelines");

        migrationBuilder.DropForeignKey(
            name: "FK_PipelineStepRuns_PipelineRuns_PipelineRunId",
            table: "PipelineStepRuns");

        migrationBuilder.DropForeignKey(
            name: "FK_Tasks_PipelineRuns_PipelineRunId",
            table: "Tasks");

        migrationBuilder.DropForeignKey(
            name: "FK_PipelineStepRuns_Tasks_TaskId",
            table: "PipelineStepRuns");

        migrationBuilder.DropTable(
            name: "AgentPoolServers");

        migrationBuilder.DropTable(
            name: "AlertRules");

        migrationBuilder.DropTable(
            name: "ApacheModules");

        migrationBuilder.DropTable(
            name: "ApacheStates");

        migrationBuilder.DropTable(
            name: "ApacheVirtualHosts");

        migrationBuilder.DropTable(
            name: "AppSettings");

        migrationBuilder.DropTable(
            name: "AuditLogs");

        migrationBuilder.DropTable(
            name: "BranchPolicies");

        migrationBuilder.DropTable(
            name: "BranchProtectionRules");

        migrationBuilder.DropTable(
            name: "CertbotCertificates");

        migrationBuilder.DropTable(
            name: "DashboardWidgets");

        migrationBuilder.DropTable(
            name: "DockerComposeStacks");

        migrationBuilder.DropTable(
            name: "DockerContainers");

        migrationBuilder.DropTable(
            name: "DockerImages");

        migrationBuilder.DropTable(
            name: "DockerNetworks");

        migrationBuilder.DropTable(
            name: "DockerVolumes");

        migrationBuilder.DropTable(
            name: "EnvironmentChecks");

        migrationBuilder.DropTable(
            name: "EnvironmentServers");

        migrationBuilder.DropTable(
            name: "ExternalLogins");

        migrationBuilder.DropTable(
            name: "MailAccounts");

        migrationBuilder.DropTable(
            name: "MailAliases");

        migrationBuilder.DropTable(
            name: "MailStates");

        migrationBuilder.DropTable(
            name: "ModuleLinks");

        migrationBuilder.DropTable(
            name: "NotificationRules");

        migrationBuilder.DropTable(
            name: "PackageEntries");

        migrationBuilder.DropTable(
            name: "PipelineApprovals");

        migrationBuilder.DropTable(
            name: "PipelineArtifacts");

        migrationBuilder.DropTable(
            name: "PipelineTemplates");

        migrationBuilder.DropTable(
            name: "PluginRegistrations");

        migrationBuilder.DropTable(
            name: "PortsentryBlockedIps");

        migrationBuilder.DropTable(
            name: "PortsentryStates");

        migrationBuilder.DropTable(
            name: "PortsentryWhitelistIps");

        migrationBuilder.DropTable(
            name: "PullRequests");

        migrationBuilder.DropTable(
            name: "RegistrationTokens");

        migrationBuilder.DropTable(
            name: "Releases");

        migrationBuilder.DropTable(
            name: "ResourcePermissions");

        migrationBuilder.DropTable(
            name: "RkhunterScanResults");

        migrationBuilder.DropTable(
            name: "RkhunterStates");

        migrationBuilder.DropTable(
            name: "RkhunterWarnings");

        migrationBuilder.DropTable(
            name: "Secrets");

        migrationBuilder.DropTable(
            name: "ServerApps");

        migrationBuilder.DropTable(
            name: "ServerMetrics");

        migrationBuilder.DropTable(
            name: "ServerModules");

        migrationBuilder.DropTable(
            name: "ServerTokens");

        migrationBuilder.DropTable(
            name: "ServiceInfos");

        migrationBuilder.DropTable(
            name: "TaskLogs");

        migrationBuilder.DropTable(
            name: "TeamspeakChannels");

        migrationBuilder.DropTable(
            name: "TeamspeakStates");

        migrationBuilder.DropTable(
            name: "TestCases");

        migrationBuilder.DropTable(
            name: "TestResults");

        migrationBuilder.DropTable(
            name: "UserRoles");

        migrationBuilder.DropTable(
            name: "VariableLibraryEntryVersions");

        migrationBuilder.DropTable(
            name: "VaultSecretVersions");

        migrationBuilder.DropTable(
            name: "WebhookSubscriptions");

        migrationBuilder.DropTable(
            name: "WorkItems");

        migrationBuilder.DropTable(
            name: "AgentPools");

        migrationBuilder.DropTable(
            name: "GitInternalRepos");

        migrationBuilder.DropTable(
            name: "Dashboards");

        migrationBuilder.DropTable(
            name: "MailDomains");

        migrationBuilder.DropTable(
            name: "NotificationChannels");

        migrationBuilder.DropTable(
            name: "PackageFeeds");

        migrationBuilder.DropTable(
            name: "Environments");

        migrationBuilder.DropTable(
            name: "TestSuites");

        migrationBuilder.DropTable(
            name: "Roles");

        migrationBuilder.DropTable(
            name: "VariableLibraryEntries");

        migrationBuilder.DropTable(
            name: "VaultSecrets");

        migrationBuilder.DropTable(
            name: "GitConnections");

        migrationBuilder.DropTable(
            name: "Users");

        migrationBuilder.DropTable(
            name: "VariableLibraries");

        migrationBuilder.DropTable(
            name: "Vaults");

        migrationBuilder.DropTable(
            name: "ServiceConnections");

        migrationBuilder.DropTable(
            name: "Servers");

        migrationBuilder.DropTable(
            name: "Projects");

        migrationBuilder.DropTable(
            name: "PipelineRuns");

        migrationBuilder.DropTable(
            name: "Pipelines");

        migrationBuilder.DropTable(
            name: "Tasks");

        migrationBuilder.DropTable(
            name: "PipelineStepRuns");
    }
}
