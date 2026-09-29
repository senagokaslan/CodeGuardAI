using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class TestCaseConfiguration : IEntityTypeConfiguration<TestCase>
{
    public void Configure(EntityTypeBuilder<TestCase> builder)
    {
        builder.ToTable("test_cases");
        builder.HasKey(testCase => testCase.Id);

        builder.Property(testCase => testCase.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(testCase => testCase.ReviewRunId)
            .HasColumnName("review_run_id")
            .IsRequired();
        builder.Property(testCase => testCase.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(testCase => testCase.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(testCase => testCase.Target)
            .HasColumnName("target")
            .HasMaxLength(500)
            .IsRequired();
        builder.Property(testCase => testCase.Scenario)
            .HasColumnName("scenario")
            .HasMaxLength(4000)
            .IsRequired();
        builder.Property(testCase => testCase.Reason)
            .HasColumnName("reason")
            .HasMaxLength(4000)
            .IsRequired();
        builder.Property(testCase => testCase.SuggestedTestCode)
            .HasColumnName("suggested_test_code")
            .HasColumnType("text");
        builder.Property(testCase => testCase.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ReviewRun>()
            .WithMany()
            .HasForeignKey(testCase => testCase.ReviewRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_test_cases_review_runs_review_run_id");

        builder.HasIndex(testCase => testCase.ReviewRunId)
            .HasDatabaseName("ix_test_cases_review_run_id");
    }
}
