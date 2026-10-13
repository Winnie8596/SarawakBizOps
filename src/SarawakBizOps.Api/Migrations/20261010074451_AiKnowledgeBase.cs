using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SarawakBizOps.Api.Migrations
{
    /// <inheritdoc />
    public partial class AiKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSourceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourcePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Supersedes = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ChunkingConfigId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSourceDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDocumentChunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    SectionHeading = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenCount = table.Column<int>(type: "int", nullable: false),
                    Embedding = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiDocumentChunks_AiSourceDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "AiSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiSourceDocumentModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    NormalisedBrand = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalisedModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSourceDocumentModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSourceDocumentModels_AiSourceDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "AiSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiDocumentChunks_DocumentId_Ordinal",
                table: "AiDocumentChunks",
                columns: new[] { "DocumentId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSourceDocumentModels_DocumentId",
                table: "AiSourceDocumentModels",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSourceDocumentModels_NormalisedBrand_NormalisedModel",
                table: "AiSourceDocumentModels",
                columns: new[] { "NormalisedBrand", "NormalisedModel" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSourceDocuments_Slug",
                table: "AiSourceDocuments",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiDocumentChunks");

            migrationBuilder.DropTable(
                name: "AiSourceDocumentModels");

            migrationBuilder.DropTable(
                name: "AiSourceDocuments");
        }
    }
}
