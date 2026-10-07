using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeGuardAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowSafetyConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_review_runs_active_project",
                table: "review_runs",
                column: "project_id",
                unique: true,
                filter: "status IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ux_ai_model_runs_successful_test_generation",
                table: "ai_model_runs",
                columns: new[] { "review_run_id", "purpose" },
                unique: true,
                filter: "purpose = 'TestGeneration' AND status = 'Succeeded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_review_runs_active_project",
                table: "review_runs");

            migrationBuilder.DropIndex(
                name: "ux_ai_model_runs_successful_test_generation",
                table: "ai_model_runs");
        }
    }
}
