using CodeGuardAI.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeGuardAI.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(project => project.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(project => project.RepositoryPath)
            .HasColumnName("repository_path")
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(project => project.NormalizedRootPath)
            .HasColumnName("normalized_root_path")
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(project => project.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(project => project.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(project => project.NormalizedRootPath)
            .IsUnique()
            .HasDatabaseName("ux_projects_normalized_root_path");
    }
}
