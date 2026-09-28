using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <inheritdoc />
    public partial class InventoryLedgerIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_PartId",
                table: "InventoryTransactions");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Parts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Parts_QuantityOnHand_NonNegative",
                table: "Parts",
                sql: "[QuantityOnHand] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_CreatedAt",
                table: "InventoryTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_PartId_CreatedAt",
                table: "InventoryTransactions",
                columns: new[] { "PartId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Parts_QuantityOnHand_NonNegative",
                table: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_CreatedAt",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_PartId_CreatedAt",
                table: "InventoryTransactions");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Parts",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_PartId",
                table: "InventoryTransactions",
                column: "PartId");
        }
    }
}
