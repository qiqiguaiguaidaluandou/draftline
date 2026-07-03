using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Draftline.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupNameAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_exception_lookup",
                table: "Exceptions");

            migrationBuilder.DropIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "Exceptions",
                newName: "GroupName");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "BatchRegistries",
                newName: "GroupName");

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<string>(type: "text", nullable: false),
                    Flow = table.Column<int>(type: "integer", nullable: false),
                    GroupName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
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
                name: "idx_user_permission_employee",
                table: "UserPermissions",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropIndex(
                name: "idx_exception_lookup",
                table: "Exceptions");

            migrationBuilder.DropIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries");

            migrationBuilder.RenameColumn(
                name: "GroupName",
                table: "Exceptions",
                newName: "EmployeeId");

            migrationBuilder.RenameColumn(
                name: "GroupName",
                table: "BatchRegistries",
                newName: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "idx_exception_lookup",
                table: "Exceptions",
                columns: new[] { "EmployeeId", "Flow" });

            migrationBuilder.CreateIndex(
                name: "idx_batch_registry_lookup",
                table: "BatchRegistries",
                columns: new[] { "EmployeeId", "Flow" });
        }
    }
}
