using CodeGuardAI.Application.Context;
using CodeGuardAI.Application.Repositories;
using CodeGuardAI.Application.Reviews.Models;

namespace CodeGuardAI.Application.Reviews;

public enum FindingRejectionReason
{
    UnknownFile = 1,
    FileNotInContext = 2,
    InvalidLineRange = 3,
    PolicyLimit = 4
}

public readonly record struct FindingGroundingResult(
    bool IsGrounded,
    FindingRejectionReason? RejectionReason)
{
    public static FindingGroundingResult Grounded() => new(true, null);

    public static FindingGroundingResult Rejected(FindingRejectionReason reason) =>
        new(false, reason);
}

public sealed class FindingGroundingValidator
{
    public FindingGroundingResult Validate(
        LLMFinding candidate,
        ScanManifest manifest,
        RepositoryContext repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(repositoryContext);

        var isManifestFile = manifest.Entries.Any(entry =>
            entry.IsIncluded &&
            entry.RelativePath.Equals(candidate.FilePath, StringComparison.Ordinal));
        if (!isManifestFile)
        {
            return FindingGroundingResult.Rejected(FindingRejectionReason.UnknownFile);
        }

        var evidenceSegments = repositoryContext.Segments
            .Where(segment =>
                !segment.IsTruncationMarker &&
                segment.RelativePath.Equals(candidate.FilePath, StringComparison.Ordinal))
            .ToArray();
        if (evidenceSegments.Length == 0)
        {
            return FindingGroundingResult.Rejected(FindingRejectionReason.FileNotInContext);
        }

        var maximumEvidenceLine = evidenceSegments.Max(segment => segment.EndLine);
        return candidate.StartLine <= maximumEvidenceLine && candidate.EndLine <= maximumEvidenceLine
            ? FindingGroundingResult.Grounded()
            : FindingGroundingResult.Rejected(FindingRejectionReason.InvalidLineRange);
    }
}
