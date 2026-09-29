using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <summary>
    /// Phase 10: service reports move to blob storage. The FileUrl placeholder is dropped — no code path ever
    /// created a ServiceReport row, so no data is lost — and replaced by the PDF's internal key and size, the
    /// revision (bumped by each regeneration) and who generated it. Report numbers are drawn from the new
    /// ServiceReportNumbers SEQUENCE; ReportNumber and WorkOrderId were already unique.
    /// </summary>
    public partial class ServiceReportStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "ServiceReports");

            migrationBuilder.CreateSequence(
                name: "ServiceReportNumbers");

            migrationBuilder.AddColumn<string>(
                name: "BlobKey",
                table: "ServiceReports",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GeneratedByUserId",
                table: "ServiceReports",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "ServiceReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SizeBytes",
                table: "ServiceReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReports_GeneratedAt",
                table: "ServiceReports",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReports_GeneratedByUserId",
                table: "ServiceReports",
                column: "GeneratedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceReports_AspNetUsers_GeneratedByUserId",
                table: "ServiceReports",
                column: "GeneratedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceReports_AspNetUsers_GeneratedByUserId",
                table: "ServiceReports");

            migrationBuilder.DropIndex(
                name: "IX_ServiceReports_GeneratedAt",
                table: "ServiceReports");

            migrationBuilder.DropIndex(
                name: "IX_ServiceReports_GeneratedByUserId",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "BlobKey",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "GeneratedByUserId",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ServiceReports");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "ServiceReports");

            migrationBuilder.DropSequence(
                name: "ServiceReportNumbers");

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "ServiceReports",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
