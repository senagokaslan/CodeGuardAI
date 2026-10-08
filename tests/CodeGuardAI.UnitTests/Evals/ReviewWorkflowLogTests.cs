using CodeGuardAI.Application.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CodeGuardAI.UnitTests.Evals;

public sealed class ReviewWorkflowLogTests
{
    private static readonly HashSet<string> AllowedStructuredFields =
    [
        "ReviewRunId",
        "ProjectId",
        "Model",
        "PromptVersion",
        "DurationMs",
        "FindingCount",
        "RejectedFindingCount",
        "ErrorCode",
        "{OriginalFormat}"
    ];

    [Fact]
    [Trait("Category", "Eval")]
    public void Review_events_have_stable_ids_and_allowlisted_non_sensitive_fields()
    {
        var logger = new RecordingLogger();
        var metrics = new ReviewRunMetrics(
            Guid.Parse("86000000-0000-0000-0000-000000000002"),
            Guid.Parse("86000000-0000-0000-0000-000000000001"),
            "offline-eval-model",
            "review-v1",
            42,
            3,
            1);

        ReviewWorkflowLog.Started(logger, metrics);
        ReviewWorkflowLog.Completed(logger, metrics);
        ReviewWorkflowLog.Failed(logger, metrics, "ProviderUnavailable");

        Assert.Equal(
            [ReviewWorkflowEventIds.Started, ReviewWorkflowEventIds.Completed, ReviewWorkflowEventIds.Failed],
            logger.Entries.Select(entry => entry.EventId.Id));
        Assert.All(
            logger.Entries.SelectMany(entry => entry.State.Keys),
            key => Assert.Contains(key, AllowedStructuredFields));
        var completed = Assert.Single(
            logger.Entries,
            entry => entry.EventId.Id == ReviewWorkflowEventIds.Completed);
        Assert.Equal(metrics.ReviewRunId, completed.State["ReviewRunId"]);
        Assert.Equal(metrics.ProjectId, completed.State["ProjectId"]);
        Assert.Equal(metrics.Model, completed.State["Model"]);
        Assert.Equal(metrics.PromptVersion, completed.State["PromptVersion"]);
        Assert.Equal(metrics.DurationMs, completed.State["DurationMs"]);
        Assert.Equal(metrics.FindingCount, completed.State["FindingCount"]);
    }

    [Fact]
    [Trait("Category", "Eval")]
    public void Review_events_do_not_expose_prompt_secret_repository_or_finding_content()
    {
        var logger = new RecordingLogger();
        var metrics = new ReviewRunMetrics(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "safe-model",
            "review-v1",
            15,
            1,
            0);
        var sensitiveSentinels = new[]
        {
            "super-secret-api-key",
            "C:/private/customer-repository",
            "SYSTEM PROMPT PRIVATE CONTENT",
            "customer.Name.Length"
        };

        ReviewWorkflowLog.Started(logger, metrics);
        ReviewWorkflowLog.Completed(logger, metrics);
        ReviewWorkflowLog.Failed(logger, metrics, "InvalidModelResponse");

        var emitted = string.Join(
            '\n',
            logger.Entries.Select(entry =>
                entry.Message + " " + string.Join(' ', entry.State.Select(pair => $"{pair.Key}={pair.Value}"))));
        Assert.All(
            sensitiveSentinels,
            sentinel => Assert.DoesNotContain(sentinel, emitted, StringComparison.Ordinal));
        Assert.DoesNotContain("RepositoryPath", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("PromptText", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("FindingReason", emitted, StringComparison.Ordinal);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Add(new LogEntry(
                eventId,
                formatter(state, exception),
                values?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, object?>()));
        }
    }

    private sealed record LogEntry(
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> State);
}
