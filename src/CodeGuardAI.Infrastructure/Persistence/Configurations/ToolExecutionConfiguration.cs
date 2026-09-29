using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class ToolExecutionConfiguration : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.ToTable("tool_executions", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "ck_tool_executions_non_negative_duration",
                "duration_ms >= 0"));
        builder.HasKey(execution => execution.Id);

        builder.Property(execution => execution.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(execution => execution.ReviewRunId)
            .HasColumnName("review_run_id");
        builder.Property(execution => execution.ToolName)
            .HasColumnName("tool_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(execution => execution.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(execution => execution.DurationMs)
            .HasColumnName("duration_ms")
            .IsRequired();
        builder.Property(execution => execution.InputSummary)
            .HasColumnName("input_summary")
            .HasMaxLength(2000)
            .IsRequired();
        builder.Property(execution => execution.OutputSummary)
            .HasColumnName("output_summary")
            .HasMaxLength(2000)
            .IsRequired();
        builder.Property(execution => execution.ErrorType)
            .HasColumnName("error_type")
            .HasMaxLength(200);
        builder.Property(execution => execution.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ReviewRun>()
            .WithMany()
            .HasForeignKey(execution => execution.ReviewRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_tool_executions_review_runs_review_run_id");

        builder.HasIndex(execution => execution.ReviewRunId)
            .HasDatabaseName("ix_tool_executions_review_run_id");
    }
}
