using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <summary>
    /// BR-09 in SQL permissions: the stock ledger moves into its own schema, where scripts/sql/create-logins.sql
    /// DENYs the app login UPDATE and DELETE. The schema already exists wherever that script ran; EnsureSchema covers
    /// a local database that never had it.
    /// <para>
    /// The table is rebuilt in the new schema rather than moved with ALTER SCHEMA ... TRANSFER: a transfer needs
    /// CONTROL on the table, which the demo's app login (db_ddladmin) does not have, while creating a table, copying
    /// the rows with their ids (IDENTITY_INSERT needs only ALTER) and dropping the old one is within its rights. The
    /// whole migration runs in one transaction, so a failure leaves the old table as it was. The immutability
    /// trigger is recreated on the new table; dropping the old table drops its trigger with it.
    /// </para>
    /// </summary>
    public partial class LedgerSchema : Migration
    {
        private const string Table = "InventoryTransactions";
        private const string Trigger = "TR_InventoryTransactions_Immutable";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            Move(migrationBuilder, fromSchema: "dbo", toSchema: "ledger");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to dbo; the (empty) ledger schema is left for create-logins.sql, which owns its permissions.
            Move(migrationBuilder, fromSchema: "ledger", toSchema: "dbo");
        }

        private static void Move(MigrationBuilder migrationBuilder, string fromSchema, string toSchema)
        {
            migrationBuilder.CreateTable(
                name: Table,
                schema: toSchema,
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartId = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "dbo",
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "dbo",
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Same ids, so work-order lines, logs and anything a person wrote down still point at the same rows.
            // An explicit id above the current identity value moves the identity on, so new rows continue after them.
            migrationBuilder.Sql($"""
                SET IDENTITY_INSERT [{toSchema}].[{Table}] ON;
                INSERT INTO [{toSchema}].[{Table}]
                    ([Id], [PartId], [TransactionType], [Quantity], [WorkOrderId], [CreatedByUserId], [Notes], [CreatedAt])
                SELECT [Id], [PartId], [TransactionType], [Quantity], [WorkOrderId], [CreatedByUserId], [Notes], [CreatedAt]
                FROM [{fromSchema}].[{Table}];
                SET IDENTITY_INSERT [{toSchema}].[{Table}] OFF;
                """);

            migrationBuilder.DropTable(
                name: Table,
                schema: fromSchema);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_CreatedAt",
                schema: toSchema,
                table: Table,
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_CreatedByUserId",
                schema: toSchema,
                table: Table,
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_PartId_CreatedAt",
                schema: toSchema,
                table: Table,
                columns: new[] { "PartId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WorkOrderId",
                schema: toSchema,
                table: Table,
                column: "WorkOrderId");

            // The same trigger as the LedgerImmutabilityAndEquipmentSerialPerBrand migration, on the new table.
            migrationBuilder.Sql($"""
                CREATE TRIGGER [{toSchema}].[{Trigger}]
                ON [{toSchema}].[{Table}]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 50009, N'InventoryTransactions rows are immutable (BR-09). Record a correcting Adjustment instead.', 1;
                END
                """);
        }
    }
}
