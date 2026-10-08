using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using Microsoft.EntityFrameworkCore;

namespace CodeGuardAI.Infrastructure.Persistence;

public sealed class CodeGuardDbContext(DbContextOptions<CodeGuardDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ReviewRun> ReviewRuns => Set<ReviewRun>();

    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<TestCase> TestCases => Set<TestCase>();

    public DbSet<AIModelRun> AIModelRuns => Set<AIModelRun>();

    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();

    public DbSet<DocumentationReport> DocumentationReports => Set<DocumentationReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CodeGuardDbContext).Assembly);
    }
}
