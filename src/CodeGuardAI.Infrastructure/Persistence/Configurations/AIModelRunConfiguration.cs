using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class AIModelRunConfiguration : IEntityTypeConfiguration<AIModelRun>
{
    public void Configure(EntityTypeBuilder<AIModelRun> builder)
    {
        builder.ToTable("ai_model_runs", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "ck_ai_model_runs_non_negative_metrics",
                "duration_ms >= 0 AND input_chars >= 0 AND output_chars >= 0"));
        builder.HasKey(modelRun => modelRun.Id);

        builder.Property(modelRun => modelRun.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(modelRun => modelRun.ReviewRunId)
            .HasColumnName("review_run_id")
            .IsRequired();
        builder.Property(modelRun => modelRun.Purpose)
            .HasColumnName("purpose")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(modelRun => modelRun.Provider)
            .HasColumnName("provider")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(modelRun => modelRun.ModelName)
            .HasColumnName("model_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(modelRun => modelRun.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(modelRun => modelRun.DurationMs)
            .HasColumnName("duration_ms")
            .IsRequired();
        builder.Property(modelRun => modelRun.InputChars)
            .HasColumnName("input_chars")
            .IsRequired();
        builder.Property(modelRun => modelRun.OutputChars)
            .HasColumnName("output_chars")
            .IsRequired();
        builder.Property(modelRun => modelRun.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(modelRun => modelRun.ErrorType)
            .HasColumnName("error_type")
            .HasMaxLength(200);
        builder.Property(modelRun => modelRun.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ReviewRun>()
            .WithMany()
            .HasForeignKey(modelRun => modelRun.ReviewRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_ai_model_runs_review_runs_review_run_id");

        builder.HasIndex(modelRun => modelRun.ReviewRunId)
            .HasDatabaseName("ix_ai_model_runs_review_run_id");
    }
}
