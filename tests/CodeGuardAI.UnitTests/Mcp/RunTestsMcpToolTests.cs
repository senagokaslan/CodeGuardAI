using System.IO.Pipelines;
using System.Security;
using CodeGuardAI.Application.Projects;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using CodeGuardAI.Infrastructure.Tools;
using CodeGuardAI.McpHost.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace CodeGuardAI.UnitTests.Mcp;

public sealed class RunTestsMcpToolTests : IDisposable
{
    private readonly Guid _repositoryId = Guid.NewGuid();
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.RunTestsMcp",
        Guid.NewGuid().ToString("N"));

    public RunTestsMcpToolTests()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public async Task Mcp_smoke_test_exposes_only_bounded_run_tests_arguments()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runner = new RecordingRunnerTool((_, _) => Task.FromResult(
            ToolResult<TestRunnerOutput>.Success(
                new TestRunnerOutput(0, 2, 0, 0, "Passed: 2"),
                TimeSpan.Zero)));
        var runnerOptions = Options.Create(new TestRunnerOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(37)
        });
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IProjectQueries>(new FixedProjectQueries(Project()));
        builder.Services.AddSingleton<ITestRunnerTool>(runner);
        builder.Services.AddSingleton<IOptions<TestRunnerOptions>>(runnerOptions);
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream())
            .WithTools<RunTestsMcpTool>();

        using var host = builder.Build();
        await host.StartAsync(timeout.Token);
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            cancellationToken: timeout.Token);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var tool = Assert.Single(tools);
        Assert.Equal(RunTestsMcpTool.ToolName, tool.Name);
        var properties = tool.JsonSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("repositoryId", out _));
        Assert.True(properties.TryGetProperty("target", out _));
        Assert.False(properties.TryGetProperty("command", out _));
        Assert.False(properties.TryGetProperty("arguments", out _));
        Assert.False(properties.TryGetProperty("workingDirectory", out _));
        Assert.False(properties.TryGetProperty("timeout", out _));

        var result = await client.CallToolAsync(
            RunTestsMcpTool.ToolName,
            new Dictionary<string, object?>
            {
                ["repositoryId"] = _repositoryId,
                ["target"] = "tests/Allowed.Tests.csproj"
            },
            cancellationToken: timeout.Token);

        Assert.NotNull(result.StructuredContent);
        Assert.True(result.StructuredContent.Value.GetProperty("toolSucceeded").GetBoolean());
        Assert.Equal(
            RunTestsMcpTool.TestsPassedOutcome,
            result.StructuredContent.Value.GetProperty("outcome").GetString());
        Assert.Equal(_repositoryRoot, runner.LastInput!.RepositoryRoot);
        Assert.Equal("tests/Allowed.Tests.csproj", runner.LastInput.RelativeProjectPath);
        Assert.Equal(TestRunnerKind.DotNet, runner.LastInput.Runner);
        Assert.Equal(TimeSpan.FromSeconds(37), runner.LastInput.Execution.Timeout);

        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData("tests/Allowed.Tests.csproj --no-restore")]
    [InlineData("tests/Allowed.Tests.csproj;whoami")]
    [InlineData("../Outside.Tests.csproj")]
    [InlineData("C:/Outside.Tests.csproj")]
    [InlineData("tests/run.cmd")]
    public async Task Mcp_input_rejects_commands_arguments_and_unsafe_targets(string target)
    {
        var runner = new RecordingRunnerTool((_, _) => throw new InvalidOperationException());
        var tool = CreateTool(runner, new TestRunnerOptions());

        var response = await tool.RunTestsAsync(
            _repositoryId,
            target,
            CancellationToken.None);

        Assert.False(response.ToolSucceeded);
        Assert.Equal(RunTestsMcpTool.ToolErrorOutcome, response.Outcome);
        Assert.Equal(RunTestsMcpTool.InvalidRequestCode, response.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task Failed_tests_are_distinct_from_tool_infrastructure_errors_and_are_audited()
    {
        const string target = "tests/Failing.Tests.csproj";
        WriteProject(target, "Failed: 1, Passed: 2, Skipped: 0", failBuild: true);
        var writer = new RecordingExecutionWriter();
        var options = new TestRunnerOptions { DefaultTimeout = TimeSpan.FromSeconds(15) };
        var tool = CreateTool(CreateSafeRunner(options, writer), options);

        var response = await tool.RunTestsAsync(
            _repositoryId,
            target,
            CancellationToken.None);

        Assert.True(response.ToolSucceeded);
        Assert.Equal(RunTestsMcpTool.TestsFailedOutcome, response.Outcome);
        Assert.NotEqual(0, response.ExitCode);
        Assert.Equal(1, response.FailedCount);
        Assert.Null(response.ErrorCode);
        var audit = Assert.Single(writer.Records);
        Assert.Equal(ToolNames.TestRunner, audit.ToolName);
        Assert.Equal(ToolExecutionOutcome.Succeeded, audit.Outcome);
        Assert.Null(audit.ErrorCode);
    }

    [Fact]
    public async Task Tool_infrastructure_failure_has_no_test_exit_code()
    {
        var runner = new RecordingRunnerTool((_, _) => Task.FromResult(
            ToolResult<TestRunnerOutput>.Failure(
                TestRunnerErrorCodes.StartFailed,
                TimeSpan.Zero)));
        var tool = CreateTool(runner, new TestRunnerOptions());

        var response = await tool.RunTestsAsync(
            _repositoryId,
            "tests/Allowed.Tests.csproj",
            CancellationToken.None);

        Assert.False(response.ToolSucceeded);
        Assert.Equal(RunTestsMcpTool.ToolErrorOutcome, response.Outcome);
        Assert.Null(response.ExitCode);
        Assert.Equal(TestRunnerErrorCodes.StartFailed, response.ErrorCode);
    }

    [Fact]
    public async Task Shared_output_limit_is_applied_by_safe_runner()
    {
        const string target = "tests/Output.Tests.csproj";
        WriteProject(target, new string('X', 4096), failBuild: false);
        var options = new TestRunnerOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(15),
            MaxOutputCharacters = 256
        };
        var tool = CreateTool(CreateSafeRunner(options), options);

        var response = await tool.RunTestsAsync(
            _repositoryId,
            target,
            CancellationToken.None);

        Assert.True(response.ToolSucceeded);
        Assert.True(response.Truncated);
        Assert.Equal(256, response.Output!.Length);
    }

    [Fact]
    public async Task Shared_timeout_is_propagated_and_timeout_is_audited()
    {
        const string target = "tests/Timeout.Tests.csproj";
        WriteProject(target, new string('Y', 4096), failBuild: false);
        var writer = new RecordingExecutionWriter();
        var options = new TestRunnerOptions { DefaultTimeout = TimeSpan.FromMilliseconds(1) };
        var tool = CreateTool(CreateSafeRunner(options, writer), options);

        var response = await tool.RunTestsAsync(
            _repositoryId,
            target,
            CancellationToken.None);

        Assert.False(response.ToolSucceeded);
        Assert.Equal(RunTestsMcpTool.ToolErrorOutcome, response.Outcome);
        Assert.Equal(ToolErrorCodes.Timeout, response.ErrorCode);
        var audit = Assert.Single(writer.Records);
        Assert.Equal(ToolExecutionOutcome.TimedOut, audit.Outcome);
        Assert.Equal(ToolErrorCodes.Timeout, audit.ErrorCode);
    }

    [Fact]
    public async Task Mcp_cancellation_token_reaches_runner_and_is_propagated()
    {
        CancellationToken observed = default;
        var runner = new RecordingRunnerTool(async (_, cancellationToken) =>
        {
            observed = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        var tool = CreateTool(runner, new TestRunnerOptions());
        using var cancellation = new CancellationTokenSource();

        var operation = tool.RunTestsAsync(
            _repositoryId,
            "tests/Allowed.Tests.csproj",
            cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(observed.CanBeCanceled);
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public void Shared_options_reject_unbounded_timeout_values()
    {
        Assert.False(TestRunnerOptions.IsValid(new TestRunnerOptions
        {
            DefaultTimeout = TimeSpan.Zero
        }));
        Assert.False(TestRunnerOptions.IsValid(new TestRunnerOptions
        {
            DefaultTimeout = TimeSpan.FromMinutes(11)
        }));
    }

    private RunTestsMcpTool CreateTool(
        ITestRunnerTool runner,
        TestRunnerOptions options) =>
        new(
            new FixedProjectQueries(Project()),
            runner,
            Options.Create(options));

    private static SafeDotnetTestRunner CreateSafeRunner(
        TestRunnerOptions options,
        RecordingExecutionWriter? writer = null) =>
        new(
            new SafePathResolver(),
            new ToolExecutionEnvelope(
                new AllowAuthorizationPolicy(),
                writer ?? new RecordingExecutionWriter(),
                TimeProvider.System),
            Options.Create(options),
            NullLogger<SafeDotnetTestRunner>.Instance);

    private ProjectReadModel Project() =>
        new(_repositoryId, "MCP fixture", _repositoryRoot, DateTimeOffset.UtcNow);

    private void WriteProject(string relativePath, string message, bool failBuild)
    {
        var fullPath = Path.Combine(
            _repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var escapedMessage = SecurityElement.Escape(message);
        var task = failBuild
            ? $"<Error Text=\"{escapedMessage}\" />"
            : $"<Message Importance=\"high\" Text=\"{escapedMessage}\" />";
        File.WriteAllText(
            fullPath,
            $"""
            <Project>
              <Target Name="VSTest">
                {task}
              </Target>
            </Project>
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
    }

    private sealed class FixedProjectQueries(ProjectReadModel? project) : IProjectQueries
    {
        public Task<ProjectReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(project?.Id == id ? project : null);
        }

        public Task<IReadOnlyList<ProjectReadModel>> ListAsync(
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRunnerTool(
        Func<TestRunnerToolInput, CancellationToken, Task<ToolResult<TestRunnerOutput>>> execute)
        : ITestRunnerTool
    {
        public int CallCount { get; private set; }
        public TestRunnerToolInput? LastInput { get; private set; }

        public Task<ToolResult<TestRunnerOutput>> ExecuteAsync(
            TestRunnerToolInput input,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastInput = input;
            return execute(input, cancellationToken);
        }
    }

    private sealed class AllowAuthorizationPolicy : IToolAuthorizationPolicy
    {
        public ValueTask<bool> IsAuthorizedAsync(
            ToolAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingExecutionWriter : IToolExecutionWriter
    {
        public List<ToolExecutionRecord> Records { get; } = [];

        public Task RecordAsync(ToolExecutionRecord execution, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Records.Add(execution);
            return Task.CompletedTask;
        }
    }
}
