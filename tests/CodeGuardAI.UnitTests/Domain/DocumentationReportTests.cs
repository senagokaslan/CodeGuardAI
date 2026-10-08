using CodeGuardAI.Domain.Documentation;
using Xunit;

namespace CodeGuardAI.UnitTests.Domain;

public sealed class DocumentationReportTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_with_valid_values_normalizes_required_text()
    {
        var report = DocumentationReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Technical report  ",
            "  # Findings  ",
            CreatedAtUtc);

        Assert.Equal("Technical report", report.Title);
        Assert.Equal("# Findings", report.MarkdownContent);
        Assert.Equal(CreatedAtUtc, report.CreatedAtUtc);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("reviewRunId")]
    [InlineData("modelRunId")]
    public void Create_with_empty_identifier_throws(string parameterName)
    {
        var id = Guid.NewGuid();
        var reviewRunId = Guid.NewGuid();
        var modelRunId = Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(() => DocumentationReport.Create(
            parameterName == "id" ? Guid.Empty : id,
            parameterName == "reviewRunId" ? Guid.Empty : reviewRunId,
            parameterName == "modelRunId" ? Guid.Empty : modelRunId,
            "Technical report",
            "# Findings",
            CreatedAtUtc));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(null, "# Findings", "title")]
    [InlineData(" ", "# Findings", "title")]
    [InlineData("Technical report", null, "markdownContent")]
    [InlineData("Technical report", " ", "markdownContent")]
    public void Create_with_empty_required_text_throws(
        string? title,
        string? markdownContent,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() => DocumentationReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            title!,
            markdownContent!,
            CreatedAtUtc));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_with_oversized_text_throws(bool oversizedTitle)
    {
        var title = oversizedTitle
            ? new string('t', DocumentationReport.MaxTitleLength + 1)
            : "Technical report";
        var markdown = oversizedTitle
            ? "# Findings"
            : new string('m', DocumentationReport.MaxMarkdownContentLength + 1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => DocumentationReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            title,
            markdown,
            CreatedAtUtc));

        Assert.Equal(oversizedTitle ? "title" : "markdownContent", exception.ParamName);
    }

    [Fact]
    public void Create_with_non_utc_timestamp_throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => DocumentationReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Technical report",
            "# Findings",
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(3))));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }
}
