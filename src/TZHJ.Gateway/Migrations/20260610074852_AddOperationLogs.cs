using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TZHJ.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_logs",
                columns: table => new
                {
                    log_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    form_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    flow = table.Column<int>(type: "integer", nullable: false),
                    client_ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_logs", x => x.log_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_oplog_employee",
                table: "operation_logs",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "idx_oplog_time",
                table: "operation_logs",
                column: "operated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_logs");
        }
    }
}
