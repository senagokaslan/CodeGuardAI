using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using CodeGuardAI.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CodeGuardAI.UnitTests.Tools;

public sealed class ToolContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.Tools",
        Guid.NewGuid().ToString("N"));

    public ToolContractTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Typed_inputs_produce_redacted_audit_summaries()
    {
        var secretRoot = Path.Combine(_root, "customer-secret-repository");
        var fileInput = new FileReadToolInput(secretRoot, "private-name.cs");
        var scanInput = new RepositoryScanToolInput(secretRoot);
        var testInput = new TestRunnerToolInput(
            secretRoot,
            "Secret.Tests.csproj",
            TestRunnerKind.DotNet);

        var summaries = new[]
        {
            fileInput.ToRedactedAuditSummary(),
            scanInput.ToRedactedAuditSummary(),
            testInput.ToRedactedAuditSummary()
        };

        Assert.All(summaries, summary =>
        {
            Assert.Contains("[redacted]", summary);
            Assert.DoesNotContain("customer-secret", summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-name", summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Secret.Tests", summary, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain(
            typeof(TestRunnerToolInput).GetProperties(),
            property => property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../Outside.Tests.csproj")]
    [InlineData("C:/outside/Outside.Tests.csproj")]
    [InlineData("tests/run.cmd")]
    public void Test_runner_input_rejects_unsafe_or_untyped_targets(string projectPath)
    {
        Assert.Throws<ArgumentException>(() => new TestRunnerToolInput(
            _root,
            projectPath,
            TestRunnerKind.DotNet));
    }

    [Fact]
    public async Task Authorization_is_checked_before_repository_scan()
    {
        var scanner = new RecordingScanner((_, _) =>
            Task.FromResult(Result.Success(EmptyManifest())));
        var writer = new RecordingExecutionWriter();
        var tool = CreateScanTool(scanner, new FixedAuthorizationPolicy(false), writer);

        var result = await tool.ExecuteAsync(
            new RepositoryScanToolInput(_root),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ToolErrorCodes.AuthorizationDenied, result.ErrorCode);
        Assert.Equal(0, scanner.CallCount);
        var audit = Assert.Single(writer.Records);
        Assert.Equal(ToolExecutionOutcome.Failed, audit.Outcome);
        Assert.Equal(ToolErrorCodes.AuthorizationDenied, audit.ErrorCode);
    }

    [Fact]
    public async Task Internal_timeout_returns_typed_result_and_audit_record()
    {
        var scanner = new RecordingScanner(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result.Success(EmptyManifest());
        });
        var writer = new RecordingExecutionWriter();
        var tool = CreateScanTool(scanner, new FixedAuthorizationPolicy(true), writer);

        var result = await tool.ExecuteAsync(
            new RepositoryScanToolInput(
                _root,
                new ToolExecutionOptions(TimeSpan.FromMilliseconds(20))),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ToolErrorCodes.Timeout, result.ErrorCode);
        Assert.True(result.Duration >= TimeSpan.Zero);
        Assert.False(result.Truncated);
        var audit = Assert.Single(writer.Records);
        Assert.Equal(ToolExecutionOutcome.TimedOut, audit.Outcome);
        Assert.Equal(ToolErrorCodes.Timeout, audit.ErrorCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_to_operation()
    {
        CancellationToken observed = default;
        var scanner = new RecordingScanner(async (_, cancellationToken) =>
        {
            observed = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result.Success(EmptyManifest());
        });
        var tool = CreateScanTool(
            scanner,
            new FixedAuthorizationPolicy(true),
            new RecordingExecutionWriter());
        using var cancellation = new CancellationTokenSource();

        var operation = tool.ExecuteAsync(
            new RepositoryScanToolInput(_root),
            cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(observed.CanBeCanceled);
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public async Task File_content_and_names_are_absent_from_audit_and_logs()
    {
        const string secretContent = "TOP-SECRET-CONTENT";
        const string secretFileName = "customer-private.cs";
        await File.WriteAllTextAsync(Path.Combine(_root, secretFileName), secretContent);
        var writer = new RecordingExecutionWriter();
        var logger = new RecordingLogger<SafeFileReadTool>();
        var envelope = new ToolExecutionEnvelope(
            new FixedAuthorizationPolicy(true),
            writer,
            TimeProvider.System);
        var tool = new SafeFileReadTool(new SafePathResolver(), envelope, logger);

        var result = await tool.ExecuteAsync(
            new FileReadToolInput(_root, secretFileName),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(secretContent, result.Value.Content);
        Assert.False(result.Truncated);
        var audit = Assert.Single(writer.Records);
        var auditText = $"{audit.RedactedInputSummary} {audit.RedactedOutputSummary}";
        Assert.DoesNotContain(secretContent, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(secretFileName, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, auditText, StringComparison.OrdinalIgnoreCase);
        var loggedText = string.Join(' ', logger.Messages.Concat(logger.Scopes));
        Assert.DoesNotContain(secretContent, loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain(secretFileName, loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, loggedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repository=[redacted]", loggedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_limit_is_reported_as_truncated()
    {
        var manifest = new ScanManifest(
            [
                new ScanManifestEntry(
                    "src/A.cs",
                    10,
                    RepositoryLanguage.CSharp,
                    ScanEntryKind.File,
                    ScanSkipReason.FileCountLimit)
            ],
            0,
            0);
        var tool = CreateScanTool(
            new RecordingScanner((_, _) => Task.FromResult(Result.Success(manifest))),
            new FixedAuthorizationPolicy(true),
            new RecordingExecutionWriter());

        var result = await tool.ExecuteAsync(
            new RepositoryScanToolInput(_root),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Truncated);
    }

    private static RepositoryScanTool CreateScanTool(
        IRepositoryScanner scanner,
        IToolAuthorizationPolicy authorization,
        IToolExecutionWriter writer)
    {
        return new RepositoryScanTool(
            scanner,
            new ToolExecutionEnvelope(authorization, writer, TimeProvider.System),
            new RecordingLogger<RepositoryScanTool>());
    }

    private static ScanManifest EmptyManifest() => new([], 0, 0);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedAuthorizationPolicy(bool allowed) : IToolAuthorizationPolicy
    {
        public ValueTask<bool> IsAuthorizedAsync(
            ToolAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(allowed);
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

    private sealed class RecordingScanner(
        Func<string, CancellationToken, Task<Result<ScanManifest>>> execute) : IRepositoryScanner
    {
        public int CallCount { get; private set; }

        public Task<Result<ScanManifest>> ScanAsync(
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return execute(repositoryRoot, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<string> Scopes { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(FormatState(state));
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private static string FormatState<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                return string.Join(';', values.Select(value => $"{value.Key}={value.Value}"));
            }

            return state?.ToString() ?? string.Empty;
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }
}
