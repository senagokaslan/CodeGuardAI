using System.IO.Pipelines;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using CodeGuardAI.Infrastructure.Tools;
using CodeGuardAI.McpHost.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace CodeGuardAI.UnitTests.Mcp;

public sealed class ReadFileMcpToolTests : IDisposable
{
    private readonly Guid _repositoryId = Guid.NewGuid();
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.Mcp",
        Guid.NewGuid().ToString("N"));

    public ReadFileMcpToolTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task Mcp_smoke_test_discovers_and_calls_read_file_over_protocol()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await File.WriteAllTextAsync(
            Path.Combine(_repositoryRoot, "Example.cs"),
            "sealed class Example;");
        var projectQueries = new FixedProjectQueries(Project());
        var fileReadTool = new SafeFileReadTool(new SafePathResolver());
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IProjectQueries>(projectQueries);
        builder.Services.AddSingleton<IFileReadTool>(fileReadTool);
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream())
            .WithTools<ReadFileMcpTool>();

        using var host = builder.Build();
        await host.StartAsync(timeout.Token);
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            cancellationToken: timeout.Token);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var tool = Assert.Single(tools);
        Assert.Equal(ReadFileMcpTool.ToolName, tool.Name);
        var properties = tool.JsonSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("repositoryId", out _));
        Assert.True(properties.TryGetProperty("relativePath", out _));
        Assert.False(properties.TryGetProperty("repositoryRoot", out _));

        var result = await client.CallToolAsync(
            ReadFileMcpTool.ToolName,
            new Dictionary<string, object?>
            {
                ["repositoryId"] = _repositoryId,
                ["relativePath"] = "Example.cs"
            },
            cancellationToken: timeout.Token);

        Assert.NotNull(result.StructuredContent);
        Assert.True(result.StructuredContent.Value.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(
            "Example.cs",
            result.StructuredContent.Value.GetProperty("relativePath").GetString());
        Assert.Equal(
            "sealed class Example;",
            result.StructuredContent.Value.GetProperty("content").GetString());

        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData(".env")]
    public async Task Mcp_adapter_reuses_safe_file_tool_security_policy(string relativePath)
    {
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, ".env"), "SECRET=value");
        var tool = CreateTool(new FixedProjectQueries(Project()));

        var response = await tool.ReadFileAsync(
            _repositoryId,
            relativePath,
            CancellationToken.None);

        Assert.False(response.IsSuccess);
        Assert.Equal(FileReadErrorCodes.InvalidPath, response.ErrorCode);
        Assert.Null(response.Content);
    }

    [Fact]
    public async Task Mcp_adapter_resolves_registered_root_instead_of_accepting_it_from_input()
    {
        await File.WriteAllTextAsync(Path.Combine(_repositoryRoot, "Allowed.cs"), "allowed");
        var queries = new FixedProjectQueries(Project());
        var tool = CreateTool(queries);

        var response = await tool.ReadFileAsync(
            _repositoryId,
            "Allowed.cs",
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(_repositoryId, queries.RequestedId);
        Assert.Equal("allowed", response.Content);
    }

    [Fact]
    public async Task Mcp_adapter_returns_structured_error_without_raw_exception_details()
    {
        const string privateException = "private-database-host:5432";
        var tool = CreateTool(new ThrowingProjectQueries(privateException));

        var response = await tool.ReadFileAsync(
            _repositoryId,
            "Example.cs",
            CancellationToken.None);

        Assert.False(response.IsSuccess);
        Assert.Equal(ReadFileMcpTool.UnexpectedFailureCode, response.ErrorCode);
        Assert.DoesNotContain(privateException, response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_adapter_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var tool = CreateTool(new FixedProjectQueries(Project()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.ReadFileAsync(
            _repositoryId,
            "Example.cs",
            cancellation.Token));
    }

    private ReadFileMcpTool CreateTool(IProjectQueries projectQueries) =>
        new(projectQueries, new SafeFileReadTool(new SafePathResolver()));

    private ProjectReadModel Project() =>
        new(_repositoryId, "MCP fixture", _repositoryRoot, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
    }

    private sealed class FixedProjectQueries(ProjectReadModel? project) : IProjectQueries
    {
        public Guid? RequestedId { get; private set; }

        public Task<ProjectReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedId = id;
            return Task.FromResult(project?.Id == id ? project : null);
        }

        public Task<IReadOnlyList<ProjectReadModel>> ListAsync(
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingProjectQueries(string message) : IProjectQueries
    {
        public Task<ProjectReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);

        public Task<IReadOnlyList<ProjectReadModel>> ListAsync(
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
