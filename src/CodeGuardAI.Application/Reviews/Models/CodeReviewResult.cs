using System.Text.Json.Serialization;

namespace CodeGuardAI.Application.Reviews.Models;

public sealed record CodeReviewResult
{
    [JsonConstructor]
    public CodeReviewResult(IReadOnlyList<LLMFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Any(finding => finding is null))
        {
            throw new ArgumentException("Findings cannot contain null entries.", nameof(findings));
        }

        Findings = findings.ToArray();
    }

    public IReadOnlyList<LLMFinding> Findings { get; }
}
