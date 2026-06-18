using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TZHJ.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeSchemaAndLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_records");

            migrationBuilder.DropTable(
                name: "operation_logs");

            migrationBuilder.DropIndex(
                name: "idx_exception_lookup",
                table: "Exceptions");

            migrationBuilder.DropIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries");

            migrationBuilder.AddColumn<string>(
                name: "ResolutionAuditId",
                table: "Exceptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Exceptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedBy",
                table: "Exceptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Exceptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "BatchRegistries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "TotalRows",
                table: "BatchRegistries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmployeeId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Flow = table.Column<int>(type: "integer", nullable: true),
                    GroupName = table.Column<string>(type: "text", nullable: true),
                    BatchId = table.Column<string>(type: "text", nullable: true),
                    ImpactCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ClientIp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_exception_lookup",
                table: "Exceptions",
                columns: new[] { "Flow", "GroupName", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries",
                columns: new[] { "Flow", "GroupName", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_activity_log_batch_action",
                table: "ActivityLogs",
                columns: new[] { "BatchId", "Action" });

            migrationBuilder.CreateIndex(
                name: "idx_activity_log_user_time",
                table: "ActivityLogs",
                columns: new[] { "EmployeeId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropIndex(
                name: "idx_exception_lookup",
                table: "Exceptions");

            migrationBuilder.DropIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries");

            migrationBuilder.DropColumn(
                name: "ResolutionAuditId",
                table: "Exceptions");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Exceptions");

            migrationBuilder.DropColumn(
                name: "ResolvedBy",
                table: "Exceptions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Exceptions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "BatchRegistries");

            migrationBuilder.DropColumn(
                name: "TotalRows",
                table: "BatchRegistries");

            migrationBuilder.CreateTable(
                name: "audit_records",
                columns: table => new
                {
                    audit_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    batch_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    employee_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    flow = table.Column<int>(type: "integer", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    window_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_records", x => x.audit_id);
                });

            migrationBuilder.CreateTable(
                name: "operation_logs",
                columns: table => new
                {
                    log_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    flow = table.Column<int>(type: "integer", nullable: false),
                    form_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    operated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_logs", x => x.log_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_exception_lookup",
                table: "Exceptions",
                columns: new[] { "Flow", "GroupName" });

            migrationBuilder.CreateIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries",
                columns: new[] { "Flow", "GroupName" });

            migrationBuilder.CreateIndex(
                name: "idx_audit_lookup",
                table: "audit_records",
                columns: new[] { "flow", "employee_id", "window_start", "window_end" });

            migrationBuilder.CreateIndex(
                name: "idx_oplog_employee",
                table: "operation_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "idx_oplog_time",
                table: "operation_logs",
                column: "operated_at");
        }
    }
}
