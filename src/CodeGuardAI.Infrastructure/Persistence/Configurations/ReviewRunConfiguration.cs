using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class ReviewRunConfiguration : IEntityTypeConfiguration<ReviewRun>
{
    public void Configure(EntityTypeBuilder<ReviewRun> builder)
    {
        builder.ToTable("review_runs");
        builder.HasKey(reviewRun => reviewRun.Id);

        builder.Property(reviewRun => reviewRun.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(reviewRun => reviewRun.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();
        builder.Property(reviewRun => reviewRun.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(reviewRun => reviewRun.ModelName)
            .HasColumnName("model_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(reviewRun => reviewRun.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(reviewRun => reviewRun.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reviewRun => reviewRun.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasColumnType("timestamp with time zone");
        builder.Property(reviewRun => reviewRun.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(100);
        builder.Property(reviewRun => reviewRun.ScanSummaryJson)
            .HasColumnName("scan_summary_json")
            .HasColumnType("jsonb");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(reviewRun => reviewRun.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_review_runs_projects_project_id");

        builder.HasIndex(reviewRun => reviewRun.ProjectId)
            .HasDatabaseName("ix_review_runs_project_id");
    }
}
