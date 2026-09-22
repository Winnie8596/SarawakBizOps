using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowCancellationAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ServiceRequestId",
                table: "WorkOrders");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "WorkOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "WorkOrders",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "ServiceRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "ServiceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "ServiceRequests",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ServiceRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CancelledByUserId",
                table: "WorkOrders",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ServiceRequestId",
                table: "WorkOrders",
                column: "ServiceRequestId",
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_CancelledByUserId",
                table: "ServiceRequests",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_AspNetUsers_CancelledByUserId",
                table: "ServiceRequests",
                column: "CancelledByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_AspNetUsers_CancelledByUserId",
                table: "WorkOrders",
                column: "CancelledByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_AspNetUsers_CancelledByUserId",
                table: "ServiceRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_AspNetUsers_CancelledByUserId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_CancelledByUserId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ServiceRequestId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_CancelledByUserId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ServiceRequests");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ServiceRequestId",
                table: "WorkOrders",
                column: "ServiceRequestId",
                unique: true);
        }
    }
}
