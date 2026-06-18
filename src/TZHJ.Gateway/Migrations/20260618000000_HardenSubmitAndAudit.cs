using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TZHJ.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class HardenSubmitAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 结构化审计列：回传成功时填充，替代 Payload 文本匹配（changes/009）
            migrationBuilder.AddColumn<string>(
                name: "AuditId",
                table: "ActivityLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowStart",
                table: "ActivityLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowEnd",
                table: "ActivityLogs",
                type: "timestamp with time zone",
                nullable: true);

            // 批次幂等回显：提交成功记录 AuditId
            migrationBuilder.AddColumn<string>(
                name: "AuditId",
                table: "BatchRegistries",
                type: "text",
                nullable: true);

            // 补拉判据精确查询索引
            migrationBuilder.CreateIndex(
                name: "idx_activity_log_audit_lookup",
                table: "ActivityLogs",
                columns: new[] { "EmployeeId", "Action", "WindowStart" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_activity_log_audit_lookup",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "AuditId",
                table: "BatchRegistries");

            migrationBuilder.DropColumn(
                name: "WindowEnd",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "WindowStart",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "AuditId",
                table: "ActivityLogs");
        }
    }
}
