using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TZHJ.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class FixBatchRegistryCompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_BatchRegistries",
                table: "BatchRegistries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BatchRegistries",
                table: "BatchRegistries",
                columns: new[] { "BatchId", "Flow", "GroupName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_BatchRegistries",
                table: "BatchRegistries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BatchRegistries",
                table: "BatchRegistries",
                column: "BatchId");
        }
    }
}
