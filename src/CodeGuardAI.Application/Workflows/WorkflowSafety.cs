namespace CodeGuardAI.Application.Workflows;

public sealed class WorkflowSafetyOptions
{
    public const string SectionName = "WorkflowSafety";

    public TimeSpan StaleReviewTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public static bool IsValid(WorkflowSafetyOptions options) =>
        options.StaleReviewTimeout >= TimeSpan.FromMinutes(1) &&
        options.StaleReviewTimeout <= TimeSpan.FromHours(24);
}

public sealed class WorkflowStepLimitExceededException(int maxSteps, string step)
    : InvalidOperationException($"Workflow step limit {maxSteps} was exceeded before '{step}'.")
{
    public int MaxSteps { get; } = maxSteps;
    public string Step { get; } = step;
}

public sealed class WorkflowStepBudget
{
    public WorkflowStepBudget(int maxSteps)
    {
        if (maxSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSteps));
        }

        MaxSteps = maxSteps;
    }

    public int MaxSteps { get; }
    public int StepsUsed { get; private set; }

    public void Enter(string step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        if (StepsUsed >= MaxSteps)
        {
            throw new WorkflowStepLimitExceededException(MaxSteps, step);
        }

        StepsUsed++;
    }
}

public enum ReviewWorkflowStage
{
    Created = 0,
    PendingPersisted = 1,
    Running = 2,
    RepositoryScanned = 3,
    ContextBuilt = 4,
    AgentCompleted = 5,
    Terminal = 6
}

public sealed class ReviewWorkflowStateMachine
{
    public const int MaxSteps = 6;

    private readonly WorkflowStepBudget _budget;

    public ReviewWorkflowStateMachine(int maxSteps = MaxSteps)
    {
        _budget = new WorkflowStepBudget(maxSteps);
    }

    public ReviewWorkflowStage Stage { get; private set; } = ReviewWorkflowStage.Created;
    public int StepsUsed => _budget.StepsUsed;

    public void MoveTo(ReviewWorkflowStage next)
    {
        if (!IsAllowed(Stage, next))
        {
            throw new InvalidOperationException(
                $"Review workflow transition '{Stage}' -> '{next}' is not allowed.");
        }

        _budget.Enter(next.ToString());
        Stage = next;
    }

    private static bool IsAllowed(ReviewWorkflowStage current, ReviewWorkflowStage next) =>
        (next == ReviewWorkflowStage.Terminal && current != ReviewWorkflowStage.Terminal) ||
        (current, next) switch
        {
            (ReviewWorkflowStage.Created, ReviewWorkflowStage.PendingPersisted) => true,
            (ReviewWorkflowStage.PendingPersisted, ReviewWorkflowStage.Running) => true,
            (ReviewWorkflowStage.Running, ReviewWorkflowStage.RepositoryScanned) => true,
            (ReviewWorkflowStage.RepositoryScanned, ReviewWorkflowStage.ContextBuilt) => true,
            (ReviewWorkflowStage.ContextBuilt, ReviewWorkflowStage.AgentCompleted) => true,
            _ => false
        };
}
