using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeGuardAI.Infrastructure.Tools;

public sealed partial class SafeDotnetTestRunner : ITestRunnerTool
{
    private readonly SafePathResolver _safePathResolver;
    private readonly ToolExecutionEnvelope _envelope;
    private readonly TestRunnerOptions _options;
    private readonly ILogger<SafeDotnetTestRunner> _logger;

    public SafeDotnetTestRunner(
        SafePathResolver safePathResolver,
        ToolExecutionEnvelope envelope,
        IOptions<TestRunnerOptions> options,
        ILogger<SafeDotnetTestRunner> logger)
    {
        _safePathResolver = safePathResolver;
        _envelope = envelope;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ToolResult<TestRunnerOutput>> ExecuteAsync(
        TestRunnerToolInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _envelope.ExecuteAsync(
            ToolNames.TestRunner,
            input.ToRedactedAuditSummary(),
            input.Execution,
            _logger,
            token => RunCoreAsync(input, token),
            cancellationToken);
    }

    private async Task<ToolOperationResult<TestRunnerOutput>> RunCoreAsync(
        TestRunnerToolInput input,
        CancellationToken cancellationToken)
    {
        if (input.Runner != TestRunnerKind.DotNet || !TestRunnerOptions.IsValid(_options))
        {
            return ToolOperationResult<TestRunnerOutput>.Failure(TestRunnerErrorCodes.StartFailed);
        }

        var root = _safePathResolver.ResolveRoot(input.RepositoryRoot);
        if (!root.IsSuccess)
        {
            return ToolOperationResult<TestRunnerOutput>.Failure(TestRunnerErrorCodes.InvalidTarget);
        }

        var project = _safePathResolver.ResolvePath(root.FullPath!, input.RelativeProjectPath);
        if (!project.IsSuccess ||
            !File.Exists(project.FullPath) ||
            !Path.GetExtension(project.FullPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ToolOperationResult<TestRunnerOutput>.Failure(TestRunnerErrorCodes.InvalidTarget);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DotNetExecutablePath.Trim(),
            WorkingDirectory = root.FullPath!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(project.FullPath!);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return ToolOperationResult<TestRunnerOutput>.Failure(TestRunnerErrorCodes.StartFailed);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ToolOperationResult<TestRunnerOutput>.Failure(TestRunnerErrorCodes.StartFailed);
        }

        var output = new BoundedOutputCollector(_options.MaxOutputCharacters);
        var stdout = DrainAsync(process.StandardOutput, output);
        var stderr = DrainAsync(process.StandardError, output);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }

        var capturedOutput = output.ToString();
        var counts = ParseCounts(capturedOutput);
        var result = new TestRunnerOutput(
            process.ExitCode,
            counts.Passed,
            counts.Failed,
            counts.Skipped,
            capturedOutput);
        return ToolOperationResult<TestRunnerOutput>.Success(
            result,
            $"success=true; exitCode={result.ExitCode}; passed={result.PassedCount}; failed={result.FailedCount}; skipped={result.SkippedCount}; truncated={output.IsTruncated.ToString().ToLowerInvariant()}",
            output.IsTruncated);
    }

    private static async Task DrainAsync(
        StreamReader reader,
        BoundedOutputCollector output)
    {
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0)
            {
                return;
            }

            output.Append(buffer.AsSpan(0, read));
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static (int Passed, int Failed, int Skipped) ParseCounts(string output)
    {
        var match = TestSummaryRegex().Match(output);
        return match.Success
            ? (
                int.Parse(match.Groups["passed"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["failed"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["skipped"].Value, CultureInfo.InvariantCulture))
            : (0, 0, 0);
    }

    [GeneratedRegex(
        @"Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TestSummaryRegex();

    private sealed class BoundedOutputCollector(int capacity)
    {
        private readonly StringBuilder _builder = new(Math.Min(capacity, 4096));
        private readonly object _gate = new();

        public bool IsTruncated { get; private set; }

        public void Append(ReadOnlySpan<char> value)
        {
            lock (_gate)
            {
                var remaining = capacity - _builder.Length;
                if (remaining <= 0)
                {
                    IsTruncated = true;
                    return;
                }

                var length = Math.Min(remaining, value.Length);
                _builder.Append(value[..length]);
                IsTruncated |= length < value.Length;
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }
}
