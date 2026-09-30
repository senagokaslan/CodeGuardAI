using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeGuardAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    repository_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_root_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scan_summary_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_runs_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_model_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    input_chars = table.Column<int>(type: "integer", nullable: false),
                    output_chars = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_model_runs", x => x.id);
                    table.CheckConstraint("ck_ai_model_runs_non_negative_metrics", "duration_ms >= 0 AND input_chars >= 0 AND output_chars >= 0");
                    table.ForeignKey(
                        name: "fk_ai_model_runs_review_runs_review_run_id",
                        column: x => x.review_run_id,
                        principalTable: "review_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "findings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    start_line = table.Column<int>(type: "integer", nullable: false),
                    end_line = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    suggestion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_findings", x => x.id);
                    table.CheckConstraint("ck_findings_confidence", "confidence >= 0 AND confidence <= 1");
                    table.CheckConstraint("ck_findings_line_range", "start_line >= 1 AND end_line >= start_line");
                    table.ForeignKey(
                        name: "fk_findings_review_runs_review_run_id",
                        column: x => x.review_run_id,
                        principalTable: "review_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "test_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    scenario = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    suggested_test_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_test_cases_review_runs_review_run_id",
                        column: x => x.review_run_id,
                        principalTable: "review_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tool_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    input_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    output_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    error_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_executions", x => x.id);
                    table.CheckConstraint("ck_tool_executions_non_negative_duration", "duration_ms >= 0");
                    table.ForeignKey(
                        name: "fk_tool_executions_review_runs_review_run_id",
                        column: x => x.review_run_id,
                        principalTable: "review_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_model_runs_review_run_id",
                table: "ai_model_runs",
                column: "review_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_findings_review_run_id",
                table: "findings",
                column: "review_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_projects_normalized_root_path",
                table: "projects",
                column: "normalized_root_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_runs_project_id",
                table: "review_runs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_cases_review_run_id",
                table: "test_cases",
                column: "review_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_executions_review_run_id",
                table: "tool_executions",
                column: "review_run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_model_runs");

            migrationBuilder.DropTable(
                name: "findings");

            migrationBuilder.DropTable(
                name: "test_cases");

            migrationBuilder.DropTable(
                name: "tool_executions");

            migrationBuilder.DropTable(
                name: "review_runs");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
