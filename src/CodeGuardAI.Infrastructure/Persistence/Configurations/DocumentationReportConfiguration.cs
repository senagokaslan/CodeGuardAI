using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class DocumentationReportConfiguration : IEntityTypeConfiguration<DocumentationReport>
{
    public void Configure(EntityTypeBuilder<DocumentationReport> builder)
    {
        builder.ToTable("documentation_reports");
        builder.HasKey(report => report.Id);

        builder.Property(report => report.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(report => report.ReviewRunId)
            .HasColumnName("review_run_id")
            .IsRequired();
        builder.Property(report => report.ModelRunId)
            .HasColumnName("model_run_id")
            .IsRequired();
        builder.Property(report => report.Title)
            .HasColumnName("title")
            .HasMaxLength(DocumentationReport.MaxTitleLength)
            .IsRequired();
        builder.Property(report => report.MarkdownContent)
            .HasColumnName("markdown_content")
            .HasColumnType("text")
            .HasMaxLength(DocumentationReport.MaxMarkdownContentLength)
            .IsRequired();
        builder.Property(report => report.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ReviewRun>()
            .WithMany()
            .HasForeignKey(report => report.ReviewRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_documentation_reports_review_runs_review_run_id");

        builder.HasOne<AIModelRun>()
            .WithMany()
            .HasForeignKey(report => report.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_documentation_reports_ai_model_runs_model_run_id");

        builder.HasIndex(report => report.ReviewRunId)
            .IsUnique()
            .HasDatabaseName("ux_documentation_reports_review_run_id");

        builder.HasIndex(report => report.ModelRunId)
            .HasDatabaseName("ix_documentation_reports_model_run_id");
    }
}
