using CodeGuardAI.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("findings", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_findings_confidence", "confidence >= 0 AND confidence <= 1");
            tableBuilder.HasCheckConstraint("ck_findings_line_range", "start_line >= 1 AND end_line >= start_line");
        });
        builder.HasKey(finding => finding.Id);

        builder.Property(finding => finding.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(finding => finding.ReviewRunId)
            .HasColumnName("review_run_id")
            .IsRequired();
        builder.Property(finding => finding.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(finding => finding.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(finding => finding.FilePath)
            .HasColumnName("file_path")
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(finding => finding.StartLine)
            .HasColumnName("start_line")
            .IsRequired();
        builder.Property(finding => finding.EndLine)
            .HasColumnName("end_line")
            .IsRequired();
        builder.Property(finding => finding.Title)
            .HasColumnName("title")
            .HasMaxLength(300)
            .IsRequired();
        builder.Property(finding => finding.Reason)
            .HasColumnName("reason")
            .HasMaxLength(4000)
            .IsRequired();
        builder.Property(finding => finding.Suggestion)
            .HasColumnName("suggestion")
            .HasMaxLength(4000)
            .IsRequired();
        builder.Property(finding => finding.Confidence)
            .HasColumnName("confidence")
            .HasPrecision(5, 4)
            .IsRequired();
        builder.Property(finding => finding.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ReviewRun>()
            .WithMany()
            .HasForeignKey(finding => finding.ReviewRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_findings_review_runs_review_run_id");

        builder.HasIndex(finding => finding.ReviewRunId)
            .HasDatabaseName("ix_findings_review_run_id");
    }
}
