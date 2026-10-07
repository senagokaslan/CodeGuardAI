using CodeGuardAI.Application.Options;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Projects;
using CodeGuardAI.Domain.Reviews;
using CodeGuardAI.Domain.Tests;
using CodeGuardAI.Infrastructure;
using CodeGuardAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodeGuardAI.UnitTests.Persistence;

public sealed class CodeGuardDbContextModelTests
{
    [Fact]
    public void Model_contains_all_aggregate_tables_and_primary_keys()
    {
        using var context = CreateContext();
        var expected = new Dictionary<Type, string>
        {
            [typeof(Project)] = "projects",
            [typeof(ReviewRun)] = "review_runs",
            [typeof(Finding)] = "findings",
            [typeof(TestCase)] = "test_cases",
            [typeof(AIModelRun)] = "ai_model_runs",
            [typeof(ToolExecution)] = "tool_executions"
        };

        foreach (var (entityType, tableName) in expected)
        {
            var metadata = context.Model.FindEntityType(entityType);

            Assert.NotNull(metadata);
            Assert.Equal(tableName, metadata.GetTableName());
            Assert.Equal("Id", Assert.Single(metadata.FindPrimaryKey()!.Properties).Name);
        }
    }

    [Fact]
    public void Project_normalized_root_path_has_unique_bounded_index()
    {
        using var context = CreateContext();
        var project = RequiredEntity<Project>(context.Model);
        var rootPath = project.FindProperty(nameof(Project.NormalizedRootPath))!;
        var index = Assert.Single(project.GetIndexes(), candidate =>
            candidate.Properties.Count == 1 && candidate.Properties[0] == rootPath);

        Assert.True(index.IsUnique);
        Assert.Equal("ux_projects_normalized_root_path", index.GetDatabaseName());
        Assert.False(rootPath.IsNullable);
        Assert.Equal(2048, rootPath.GetMaxLength());
    }

    [Fact]
    public void Review_relationships_use_explicit_cascade_delete()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertCascadeForeignKey<ReviewRun, Project>(model, nameof(ReviewRun.ProjectId), required: true);
        AssertCascadeForeignKey<Finding, ReviewRun>(model, nameof(Finding.ReviewRunId), required: true);
        AssertCascadeForeignKey<TestCase, ReviewRun>(model, nameof(TestCase.ReviewRunId), required: true);
        AssertCascadeForeignKey<AIModelRun, ReviewRun>(model, nameof(AIModelRun.ReviewRunId), required: true);
        AssertCascadeForeignKey<ToolExecution, ReviewRun>(model, nameof(ToolExecution.ReviewRunId), required: false);
    }

    [Fact]
    public void Enum_and_confidence_properties_have_expected_relational_metadata()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertStringConversion<ReviewRun>(model, nameof(ReviewRun.Status), 32);
        AssertStringConversion<Finding>(model, nameof(Finding.Severity), 32);
        AssertStringConversion<Finding>(model, nameof(Finding.Category), 64);
        AssertStringConversion<TestCase>(model, nameof(TestCase.Type), 32);
        AssertStringConversion<AIModelRun>(model, nameof(AIModelRun.Purpose), 32);
        AssertStringConversion<AIModelRun>(model, nameof(AIModelRun.Status), 32);
        AssertStringConversion<ToolExecution>(model, nameof(ToolExecution.Status), 32);

        var confidence = RequiredEntity<Finding>(model).FindProperty(nameof(Finding.Confidence))!;
        Assert.Equal(5, confidence.GetPrecision());
        Assert.Equal(4, confidence.GetScale());
        Assert.False(confidence.IsNullable);
    }

    [Fact]
    public void Persistence_registration_is_scoped_and_uses_npgsql()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions
        {
            ConnectionString = "Host=localhost;Database=codeguard;Username=codeguard;Password=not-used"
        }));
        services.AddCodeGuardPersistence();

        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(CodeGuardDbContext));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<CodeGuardDbContext>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<CodeGuardDbContext>();
        using var secondScope = provider.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<CodeGuardDbContext>();

        Assert.Same(first, sameScope);
        Assert.NotSame(first, second);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", first.Database.ProviderName);
    }

    [Fact]
    public void Migration_script_contains_schema_and_workflow_concurrency_contracts()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var script = context.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Equal(2, migrations.Length);
        Assert.EndsWith("_InitialCreate", migrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_WorkflowSafetyConcurrency", migrations[1], StringComparison.Ordinal);

        var expectedTables = new[]
        {
            "projects",
            "review_runs",
            "findings",
            "test_cases",
            "ai_model_runs",
            "tool_executions"
        };
        foreach (var table in expectedTables)
        {
            Assert.Contains($"CREATE TABLE {table}", script, StringComparison.Ordinal);
        }

        Assert.Contains("ux_projects_normalized_root_path", script, StringComparison.Ordinal);
        Assert.Contains("ux_review_runs_active_project", script, StringComparison.Ordinal);
        Assert.Contains("ux_ai_model_runs_successful_test_generation", script, StringComparison.Ordinal);
        Assert.Equal(5, script.Split("ON DELETE CASCADE", StringSplitOptions.None).Length - 1);
        Assert.Contains("character varying(32)", script, StringComparison.Ordinal);
        Assert.Contains("character varying(64)", script, StringComparison.Ordinal);
    }

    private static CodeGuardDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CodeGuardDbContext>()
            .UseNpgsql("Host=localhost;Database=codeguard;Username=codeguard;Password=not-used")
            .ConfigureWarnings(warnings => warnings.Default(WarningBehavior.Throw))
            .Options;

        return new CodeGuardDbContext(options);
    }

    private static IEntityType RequiredEntity<TEntity>(IModel model)
    {
        return model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Missing entity metadata for {typeof(TEntity).Name}.");
    }

    private static void AssertCascadeForeignKey<TDependent, TPrincipal>(
        IModel model,
        string foreignKeyProperty,
        bool required)
    {
        var dependent = RequiredEntity<TDependent>(model);
        var foreignKey = Assert.Single(dependent.GetForeignKeys(), candidate =>
            candidate.PrincipalEntityType.ClrType == typeof(TPrincipal));

        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(required, foreignKey.IsRequired);
        Assert.Equal(foreignKeyProperty, Assert.Single(foreignKey.Properties).Name);
    }

    private static void AssertStringConversion<TEntity>(IModel model, string propertyName, int maxLength)
    {
        var property = RequiredEntity<TEntity>(model).FindProperty(propertyName)!;
        var converter = property.GetValueConverter() ?? property.GetTypeMapping().Converter;

        Assert.NotNull(converter);
        Assert.Equal(typeof(string), converter.ProviderClrType);
        Assert.Equal(maxLength, property.GetMaxLength());
        Assert.False(property.IsNullable);
    }
}
