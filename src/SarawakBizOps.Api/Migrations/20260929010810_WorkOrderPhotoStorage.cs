using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <summary>
    /// Phase 9: job photos move to blob storage. The FileUrl placeholder is dropped — no code path ever created
    /// a WorkOrderPhoto row, so no data is lost — and replaced by the blob's internal key, the content type
    /// detected from the bytes, and the size. The database never holds the image or a public URL (SDD §15).
    /// </summary>
    public partial class WorkOrderPhotoStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "WorkOrderPhotos");

            migrationBuilder.AddColumn<string>(
                name: "BlobKey",
                table: "WorkOrderPhotos",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "WorkOrderPhotos",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SizeBytes",
                table: "WorkOrderPhotos",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobKey",
                table: "WorkOrderPhotos");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "WorkOrderPhotos");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "WorkOrderPhotos");

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "WorkOrderPhotos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }
    }
}
