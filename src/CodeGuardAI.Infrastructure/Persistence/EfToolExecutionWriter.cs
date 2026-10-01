using CodeGuardAI.Application.Tools;
using CodeGuardAI.Domain.Observability;

namespace CodeGuardAI.Infrastructure.Persistence;

internal sealed class EfToolExecutionWriter(CodeGuardDbContext dbContext) : IToolExecutionWriter
{
    public async Task RecordAsync(ToolExecutionRecord execution, CancellationToken cancellationToken)
    {
        var status = execution.Outcome switch
        {
            ToolExecutionOutcome.Succeeded => ToolExecutionStatus.Succeeded,
            ToolExecutionOutcome.TimedOut => ToolExecutionStatus.TimedOut,
            _ => ToolExecutionStatus.Failed
        };
        dbContext.ToolExecutions.Add(ToolExecution.Create(
            execution.Id,
            execution.ReviewRunId,
            execution.ToolName,
            status,
            execution.DurationMilliseconds,
            execution.RedactedInputSummary,
            execution.RedactedOutputSummary,
            execution.ErrorCode,
            execution.CreatedAtUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
