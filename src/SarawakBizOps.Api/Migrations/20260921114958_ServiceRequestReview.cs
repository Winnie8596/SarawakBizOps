using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <inheritdoc />
    public partial class ServiceRequestReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "ServiceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByUserId",
                table: "ServiceRequests",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "ServiceRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_RejectedByUserId",
                table: "ServiceRequests",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Status",
                table: "ServiceRequests",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_AspNetUsers_RejectedByUserId",
                table: "ServiceRequests",
                column: "RejectedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_AspNetUsers_RejectedByUserId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_RejectedByUserId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_Status",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "ServiceRequests");
        }
    }
}
