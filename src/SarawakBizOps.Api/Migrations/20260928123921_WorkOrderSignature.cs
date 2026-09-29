using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <summary>
    /// Phase 8: the customer signature moves to blob storage. The unused CustomerSignatureUrl placeholder
    /// (no code path ever wrote it, so no data is lost) is replaced by the blob's internal key and metadata;
    /// the database never holds the image or a public URL (SDD §15).
    /// </summary>
    public partial class WorkOrderSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerSignatureUrl",
                table: "WorkOrders");

            migrationBuilder.AddColumn<string>(
                name: "SignatureBlobKey",
                table: "WorkOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignatureSizeBytes",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAt",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureBlobKey",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SignatureSizeBytes",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "WorkOrders");

            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureUrl",
                table: "WorkOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
