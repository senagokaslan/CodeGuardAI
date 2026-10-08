using CodeGuardAI.Domain.Reviews;
using Xunit;

namespace CodeGuardAI.UnitTests.Domain;

public sealed class FindingTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_WithConfidenceOutsideUnitInterval_Throws(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFinding(
            confidence: Convert.ToDecimal(confidence)));
    }

    [Fact]
    public void Create_WithStartLineBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFinding(startLine: 0));
    }

    [Fact]
    public void Create_WithEndLineBeforeStartLine_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFinding(startLine: 8, endLine: 7));
    }

    [Theory]
    [InlineData("C:/repo/Program.cs")]
    [InlineData("C:\\repo\\Program.cs")]
    [InlineData("/repo/Program.cs")]
    [InlineData("\\\\server\\share\\Program.cs")]
    public void Create_WithAbsoluteFilePath_Throws(string filePath)
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(filePath: filePath));
    }

    [Fact]
    public void Create_WithUndefinedEnum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFinding(severity: (FindingSeverity)999));
    }

    [Fact]
    public void Create_WithBoundaryValues_CreatesFinding()
    {
        var finding = CreateFinding(startLine: 1, endLine: 1, confidence: 1m);

        Assert.Equal("src/Program.cs", finding.FilePath);
        Assert.Equal(1, finding.StartLine);
        Assert.Equal(1, finding.EndLine);
        Assert.Equal(1m, finding.Confidence);
        Assert.Equal(TimeSpan.Zero, finding.CreatedAtUtc.Offset);
    }

    private static Finding CreateFinding(
        string filePath = "src/Program.cs",
        int startLine = 4,
        int endLine = 6,
        decimal confidence = 0.75m,
        FindingSeverity severity = FindingSeverity.High)
    {
        return Finding.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            severity,
            FindingCategory.Security,
            filePath,
            startLine,
            endLine,
            "Unsafe input",
            "Input reaches a sensitive operation.",
            "Validate the input.",
            confidence,
            CreatedAtUtc);
    }
}
