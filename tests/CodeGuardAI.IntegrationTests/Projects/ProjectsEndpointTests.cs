using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeGuardAI.Api.Contracts.Projects;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests.Projects;

public sealed class ProjectsEndpointTests
{
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Post_returns_201_with_get_location()
    {
        var service = new StubProjectService
        {
            CreateResult = Result.Success(new ProjectReadModel(
                ProjectId,
                "CodeGuard AI",
                "C:/repos/CodeGuard",
                CreatedAtUtc))
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = "CodeGuard AI",
            RepositoryPath = "C:/repos/CodeGuard"
        });
        var body = await response.Content.ReadFromJsonAsync<ProjectResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/projects/{ProjectId}", response.Headers.Location?.ToString());
        Assert.NotNull(body);
        Assert.Equal(ProjectId, body.Id);
    }

    [Fact]
    public async Task Duplicate_project_returns_409_problem_details()
    {
        var service = new StubProjectService
        {
            CreateResult = Result.Failure<ProjectReadModel>(ProjectErrors.DuplicateRepositoryPath)
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = "CodeGuard AI",
            RepositoryPath = "C:/repos/CodeGuard"
        });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "project.repository_path_conflict",
            json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_rejects_page_size_above_public_bound_before_calling_service()
    {
        var service = new StubProjectService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/projects?page=1&pageSize={ProjectLimits.MaxPageSize + 1}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.ListCallCount);
    }

    [Fact]
    public async Task Get_missing_project_returns_404_problem_details()
    {
        var service = new StubProjectService
        {
            GetResult = Result.Failure<ProjectReadModel>(ProjectErrors.NotFound)
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("project.not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Scan_uses_registered_project_root_and_returns_skip_reasons()
    {
        const string registeredRoot = "C:/normalized/repository";
        var service = new StubProjectService
        {
            GetResult = Result.Success(new ProjectReadModel(
                ProjectId,
                "CodeGuard AI",
                registeredRoot,
                CreatedAtUtc))
        };
        var scanner = new RecordingScanner(new ScanManifest(
            [
                new ScanManifestEntry("src/Program.cs", 120, RepositoryLanguage.CSharp, ScanEntryKind.File, ScanSkipReason.None),
                new ScanManifestEntry(".env", 40, RepositoryLanguage.Unknown, ScanEntryKind.File, ScanSkipReason.SensitivePath)
            ],
            1,
            120));
        await using var factory = CreateFactory(service, scanner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/projects/{ProjectId}/scan", null);
        var body = await response.Content.ReadFromJsonAsync<ProjectScanResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(registeredRoot, scanner.RepositoryRoot);
        Assert.NotNull(body);
        Assert.Equal(1, body.IncludedFileCount);
        Assert.Equal(1, body.SkippedEntryCount);
        Assert.Equal("SensitivePath", body.Entries[1].SkipReason);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        StubProjectService service,
        IRepositoryScanner? scanner = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions();
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IProjectService>();
                    services.RemoveAll<IRepositoryScanner>();
                    services.AddSingleton<IProjectService>(service);
                    services.AddSingleton(scanner ?? new RecordingScanner(new ScanManifest([], 0, 0)));
                });
            });
    }

    private sealed class StubProjectService : IProjectService
    {
        public Result<ProjectReadModel> CreateResult { get; init; } = Result.Success(new ProjectReadModel(
            ProjectId,
            "CodeGuard AI",
            "C:/repos/CodeGuard",
            CreatedAtUtc));

        public Result<ProjectReadModel> GetResult { get; init; } = Result.Failure<ProjectReadModel>(ProjectErrors.NotFound);

        public int ListCallCount { get; private set; }

        public Task<Result<ProjectReadModel>> CreateAsync(
            CreateProjectCommand command,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResult);
        }

        public Task<Result<ProjectReadModel>> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetResult);
        }

        public Task<Result<ProjectPage>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            return Task.FromResult(Result.Success(new ProjectPage([], page, pageSize)));
        }
    }

    private sealed class RecordingScanner(ScanManifest result) : IRepositoryScanner
    {
        public string? RepositoryRoot { get; private set; }

        public Task<Result<ScanManifest>> ScanAsync(
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            RepositoryRoot = repositoryRoot;
            return Task.FromResult(Result.Success(result));
        }
    }
}
