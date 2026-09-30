using System.Net;
using System.Net.Http.Json;
using CodeGuardAI.Api.Contracts.Projects;
using CodeGuardAI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace CodeGuardAI.IntegrationTests.Projects;

[Trait("Category", "Database")]
public sealed class ProjectsDatabaseTests
{
    private const string ConnectionStringVariable = "CODEGUARD_TEST_DATABASE_CONNECTION_STRING";

    [DatabaseFact]
    public async Task Create_duplicate_get_and_list_use_real_postgresql()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)!;
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.EndsWith("_test", connection.Database, StringComparison.OrdinalIgnoreCase);

        await using var factory = CreateFactory(connectionString);
        await ResetProjectsAsync(factory);

        try
        {
            using var client = factory.CreateClient();
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"CodeGuard-{Guid.NewGuid():N}");
            var request = new CreateProjectRequest
            {
                Name = "Database Project",
                RepositoryPath = repositoryPath
            };

            using var created = await client.PostAsJsonAsync("/projects", request);
            var createdProject = await created.Content.ReadFromJsonAsync<ProjectResponse>();

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.NotNull(createdProject);
            Assert.Equal($"/projects/{createdProject.Id}", created.Headers.Location?.AbsolutePath);

            using var duplicate = await client.PostAsJsonAsync("/projects", new CreateProjectRequest
            {
                Name = "Same Repository",
                RepositoryPath = repositoryPath + Path.DirectorySeparatorChar
            });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            using var get = await client.GetAsync(created.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);

            var list = await client.GetFromJsonAsync<ProjectListResponse>("/projects?page=1&pageSize=10");
            Assert.NotNull(list);
            Assert.Contains(list.Items, project => project.Id == createdProject.Id);
        }
        finally
        {
            await ResetProjectsAsync(factory);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString
                    }));
    }

    private static async Task ResetProjectsAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeGuardDbContext>();
        await dbContext.Database.MigrateAsync();
        await dbContext.Projects.ExecuteDeleteAsync();
    }

    private sealed class DatabaseFactAttribute : FactAttribute
    {
        public DatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = $"Set {ConnectionStringVariable} to run PostgreSQL integration tests.";
            }
        }
    }
}
