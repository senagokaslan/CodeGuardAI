using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeGuardAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentationReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documentation_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    markdown_content = table.Column<string>(type: "text", maxLength: 200000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentation_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentation_reports_ai_model_runs_model_run_id",
                        column: x => x.model_run_id,
                        principalTable: "ai_model_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_documentation_reports_review_runs_review_run_id",
                        column: x => x.review_run_id,
                        principalTable: "review_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documentation_reports_model_run_id",
                table: "documentation_reports",
                column: "model_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_documentation_reports_review_run_id",
                table: "documentation_reports",
                column: "review_run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentation_reports");
        }
    }
}
