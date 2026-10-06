using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <summary>
    /// BR-09 in the database: a trigger refuses every UPDATE and DELETE on the stock ledger, whoever sends it
    /// (corrections are a new Adjustment row). And equipment serial numbers become unique per brand, not globally:
    /// a looser rule, so every existing row already satisfies it.
    /// </summary>
    public partial class LedgerImmutabilityAndEquipmentSerialPerBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Equipment_SerialNumber",
                table: "Equipment");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_Brand_SerialNumber",
                table: "Equipment",
                columns: new[] { "Brand", "SerialNumber" },
                unique: true);

            // AFTER, not INSTEAD OF: the THROW rolls the statement back. The IF lets a statement that matched no
            // rows through, so it is only ever an actual change to a ledger row that fails.
            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_InventoryTransactions_Immutable]
                ON [dbo].[InventoryTransactions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 50009, N'InventoryTransactions rows are immutable (BR-09). Record a correcting Adjustment instead.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_InventoryTransactions_Immutable];");

            migrationBuilder.DropIndex(
                name: "IX_Equipment_Brand_SerialNumber",
                table: "Equipment");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_SerialNumber",
                table: "Equipment",
                column: "SerialNumber",
                unique: true);
        }
    }
}
