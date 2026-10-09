using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderApproverAndRequestJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "WorkOrders",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ApprovedByUserId",
                table: "WorkOrders",
                column: "ApprovedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_AspNetUsers_ApprovedByUserId",
                table: "WorkOrders",
                column: "ApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_AspNetUsers_ApprovedByUserId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ApprovedByUserId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "WorkOrders");
        }
    }
}
