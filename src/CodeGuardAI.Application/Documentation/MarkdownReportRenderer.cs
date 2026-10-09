using System.Text;
using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Documentation;

public sealed class MarkdownReportRenderer
{
    public Result<string> Render(
        GroundedDocumentationReport report,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (maxCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var builder = new StringBuilder(Math.Min(maxCharacters, 16_384));
        builder.Append("# ").AppendLine(EscapeText(report.Title)).AppendLine();
        builder.AppendLine("## Executive summary").AppendLine();
        AppendParagraph(builder, report.ExecutiveSummary);
        builder.AppendLine().AppendLine("## Findings").AppendLine();

        if (report.Findings.Count == 0)
        {
            builder.AppendLine("No persisted findings were recorded for this review.");
        }
        else
        {
            var orderedFindings = report.Findings
                .OrderByDescending(item => item.Finding.Severity)
                .ThenBy(item => item.Finding.FilePath, StringComparer.Ordinal)
                .ThenBy(item => item.Finding.StartLine)
                .ThenBy(item => item.Finding.Id)
                .ToArray();
            for (var index = 0; index < orderedFindings.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = orderedFindings[index];
                var finding = item.Finding;
                builder
                    .Append("### ").Append(index + 1).Append(". [")
                    .Append(finding.Severity).Append("] ")
                    .AppendLine(EscapeText(finding.Title));
                builder.Append("- Severity: ").AppendLine(finding.Severity.ToString());
                builder.Append("- Category: ").AppendLine(finding.Category.ToString());
                builder.Append("- Location: `")
                    .Append(EscapeCode(finding.FilePath)).Append(':')
                    .Append(finding.StartLine).Append('-').Append(finding.EndLine)
                    .AppendLine("`");
                builder.Append("- Reason: ");
                AppendParagraph(builder, finding.Reason);
                builder.Append("- Suggested remediation: ");
                AppendParagraph(builder, finding.Suggestion);
                builder.Append("- Impact: ");
                AppendParagraph(builder, item.Impact);
                builder.AppendLine();
                if (builder.Length > maxCharacters)
                {
                    return Result.Failure<string>(DocumentationRenderingErrors.OutputLimitExceeded);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        builder.AppendLine().AppendLine("## Test suggestions").AppendLine();
        if (report.TestCases.Count == 0)
        {
            builder.AppendLine("No persisted test suggestions were recorded for this review.");
        }
        else
        {
            var orderedTestCases = report.TestCases
                .OrderBy(item => item.TestCase.Type)
                .ThenBy(item => item.TestCase.Target, StringComparer.Ordinal)
                .ThenBy(item => item.TestCase.Name, StringComparer.Ordinal)
                .ThenBy(item => item.TestCase.Id)
                .ToArray();
            for (var index = 0; index < orderedTestCases.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = orderedTestCases[index];
                var testCase = item.TestCase;
                builder.Append("### ").Append(index + 1).Append(". ")
                    .AppendLine(EscapeText(testCase.Name));
                builder.Append("- Type: ").AppendLine(testCase.Type.ToString());
                builder.Append("- Target: `").Append(EscapeCode(testCase.Target)).AppendLine("`");
                builder.Append("- Scenario: ");
                AppendParagraph(builder, testCase.Scenario);
                builder.Append("- Reason: ");
                AppendParagraph(builder, testCase.Reason);
                builder.Append("- Coverage: ");
                AppendParagraph(builder, item.Coverage);
                builder.AppendLine();
                if (builder.Length > maxCharacters)
                {
                    return Result.Failure<string>(DocumentationRenderingErrors.OutputLimitExceeded);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        builder.AppendLine().AppendLine("## Conclusion").AppendLine();
        AppendParagraph(builder, report.Conclusion);
        var markdown = builder.ToString().TrimEnd();
        return markdown.Length <= maxCharacters
            ? Result.Success(markdown)
            : Result.Failure<string>(DocumentationRenderingErrors.OutputLimitExceeded);
    }

    private static void AppendParagraph(StringBuilder builder, string value)
    {
        builder.AppendLine(EscapeText(value.ReplaceLineEndings(" ")));
    }

    private static string EscapeText(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
    }

    private static string EscapeCode(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal).ReplaceLineEndings(" ");
}

public static class DocumentationRenderingErrors
{
    public static readonly Error OutputLimitExceeded = Error.Validation(
        "documentation.output_limit_exceeded",
        "The rendered documentation report exceeds the configured output limit.");
}
