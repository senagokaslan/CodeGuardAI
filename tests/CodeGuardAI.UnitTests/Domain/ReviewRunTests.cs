using CodeGuardAI.Domain.Reviews;
using Xunit;

namespace CodeGuardAI.UnitTests.Domain;

public sealed class ReviewRunTests
{
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_CreatesRunningReview()
    {
        var review = CreateReview();

        Assert.Equal(ReviewStatus.Running, review.Status);
        Assert.Null(review.CompletedAtUtc);
        Assert.Null(review.ErrorCode);
    }

    [Fact]
    public void TryComplete_FromRunning_CompletesReview()
    {
        var review = CreateReview();
        var completedAtUtc = StartedAtUtc.AddSeconds(5);

        var changed = review.TryComplete(completedAtUtc, "{\"files\":2}");

        Assert.True(changed);
        Assert.Equal(ReviewStatus.Completed, review.Status);
        Assert.Equal(completedAtUtc, review.CompletedAtUtc);
        Assert.Equal("{\"files\":2}", review.ScanSummaryJson);
        Assert.Null(review.ErrorCode);
    }

    [Fact]
    public void TryFail_WithCancelledCode_RecordsCancellationAsFailed()
    {
        var review = CreateReview();
        var completedAtUtc = StartedAtUtc.AddSeconds(2);

        var changed = review.TryFail(ReviewRun.CancelledErrorCode, completedAtUtc);

        Assert.True(changed);
        Assert.Equal(ReviewStatus.Failed, review.Status);
        Assert.Equal("Cancelled", review.ErrorCode);
        Assert.Equal(completedAtUtc, review.CompletedAtUtc);
    }

    [Fact]
    public void TerminalReview_CannotTransitionAgain()
    {
        var review = CreateReview();
        var firstCompletion = StartedAtUtc.AddSeconds(1);
        Assert.True(review.TryComplete(firstCompletion));

        var changed = review.TryFail("Unexpected", StartedAtUtc.AddSeconds(2));

        Assert.False(changed);
        Assert.Equal(ReviewStatus.Completed, review.Status);
        Assert.Equal(firstCompletion, review.CompletedAtUtc);
        Assert.Null(review.ErrorCode);
    }

    [Fact]
    public void TryComplete_BeforeStart_ThrowsWithoutChangingState()
    {
        var review = CreateReview();

        Assert.Throws<ArgumentOutOfRangeException>(() => review.TryComplete(StartedAtUtc.AddTicks(-1)));
        Assert.Equal(ReviewStatus.Running, review.Status);
        Assert.Null(review.CompletedAtUtc);
    }

    [Fact]
    public void Start_WithNonUtcTimestamp_Throws()
    {
        var localOffset = new DateTimeOffset(2026, 9, 29, 13, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => ReviewRun.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "gemini",
            "v1",
            localOffset));
    }

    private static ReviewRun CreateReview()
    {
        return ReviewRun.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "gemini",
            "v1",
            StartedAtUtc);
    }
}
