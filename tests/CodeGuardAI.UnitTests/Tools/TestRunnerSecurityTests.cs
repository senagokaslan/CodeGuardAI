using System.Diagnostics;
using System.Security;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using CodeGuardAI.Infrastructure.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodeGuardAI.UnitTests.Tools;

public sealed class TestRunnerSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.TestRunnerSecurity",
        Guid.NewGuid().ToString("N"));

    public TestRunnerSecurityTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData("../Outside.Tests.csproj")]
    [InlineData("C:/outside/Outside.Tests.csproj")]
    [InlineData("tests/Run.Tests.csproj;whoami")]
    [InlineData("tests/Run&Calc.Tests.csproj")]
    [InlineData("C:drive-relative.Tests.csproj")]
    [InlineData("tests/*.Tests.csproj")]
    [InlineData("tests/Run.Tests.csproj --no-restore")]
    [InlineData("tests/run.cmd")]
    public void Input_rejects_traversal_external_paths_metacharacters_and_arguments(string path)
    {
        Assert.Throws<ArgumentException>(() => new TestRunnerToolInput(
            _root,
            path,
            TestRunnerKind.DotNet));
    }

    [Fact]
    public async Task Safe_fixture_runs_with_fixed_dotnet_test_and_returns_structured_counts()
    {
        const string relativePath = "tests/Safe Fixture.Tests.csproj";
        WriteProject(relativePath, "Failed: 0, Passed: 3, Skipped: 1");
        var writer = new RecordingExecutionWriter();
        var runner = CreateRunner(writer: writer);

        var result = await runner.ExecuteAsync(
            new TestRunnerToolInput(
                _root,
                relativePath,
                TestRunnerKind.DotNet,
                new ToolExecutionOptions(TimeSpan.FromSeconds(15))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ExitCode);
        Assert.Equal(3, result.Value.PassedCount);
        Assert.Equal(0, result.Value.FailedCount);
        Assert.Equal(1, result.Value.SkippedCount);
        Assert.Contains("Passed: 3", result.Value.Output, StringComparison.Ordinal);
        var audit = Assert.Single(writer.Records);
        Assert.DoesNotContain(_root, audit.RedactedInputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Safe Fixture", audit.RedactedInputSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_is_capped_while_process_streams_are_fully_drained()
    {
        const string relativePath = "tests/Output.Tests.csproj";
        WriteProject(relativePath, new string('X', 4096));
        var runner = CreateRunner(maxOutputCharacters: 256);

        var result = await runner.ExecuteAsync(
            new TestRunnerToolInput(
                _root,
                relativePath,
                TestRunnerKind.DotNet,
                new ToolExecutionOptions(TimeSpan.FromSeconds(15))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Truncated);
        Assert.Equal(256, result.Value.Output.Length);
    }

    [Fact]
    public async Task Timeout_kills_the_process_tree_and_returns_typed_failure()
    {
        const string relativePath = "tests/Timeout.Tests.csproj";
        WriteProject(relativePath, new string('Y', 4096));
        var writer = new RecordingExecutionWriter();
        var runner = CreateRunner(writer: writer);
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.ExecuteAsync(
            new TestRunnerToolInput(
                _root,
                relativePath,
                TestRunnerKind.DotNet,
                new ToolExecutionOptions(TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ToolErrorCodes.Timeout, result.ErrorCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        var audit = Assert.Single(writer.Records);
        Assert.Equal(ToolExecutionOutcome.TimedOut, audit.Outcome);
    }

    [Fact]
    public async Task Missing_or_external_project_is_rejected_before_process_start()
    {
        var runner = CreateRunner();

        var result = await runner.ExecuteAsync(
            new TestRunnerToolInput(
                _root,
                "tests/Missing.Tests.csproj",
                TestRunnerKind.DotNet),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TestRunnerErrorCodes.InvalidTarget, result.ErrorCode);
    }

    private SafeDotnetTestRunner CreateRunner(
        int maxOutputCharacters = TestRunnerOptions.DefaultMaxOutputCharacters,
        RecordingExecutionWriter? writer = null)
    {
        return new SafeDotnetTestRunner(
            new SafePathResolver(),
            new ToolExecutionEnvelope(
                new AllowAuthorizationPolicy(),
                writer ?? new RecordingExecutionWriter(),
                TimeProvider.System),
            Options.Create(new TestRunnerOptions
            {
                DotNetExecutablePath = "dotnet",
                MaxOutputCharacters = maxOutputCharacters
            }),
            NullLogger<SafeDotnetTestRunner>.Instance);
    }

    private void WriteProject(string relativePath, string message)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var escapedMessage = SecurityElement.Escape(message);
        File.WriteAllText(
            fullPath,
            $"""
            <Project>
              <Target Name="VSTest">
                <Message Importance="high" Text="{escapedMessage}" />
              </Target>
            </Project>
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
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
