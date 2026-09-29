using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderPendingParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingPartId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingPartQuantity",
                table: "WorkOrders",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingPartsAt",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingPartsReason",
                table: "WorkOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResumedAt",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_PendingPartId",
                table: "WorkOrders",
                column: "PendingPartId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Parts_PendingPartId",
                table: "WorkOrders",
                column: "PendingPartId",
                principalTable: "Parts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Parts_PendingPartId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_PendingPartId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PendingPartId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PendingPartQuantity",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PendingPartsAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PendingPartsReason",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ResumedAt",
                table: "WorkOrders");
        }
    }
}
