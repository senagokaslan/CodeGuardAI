using CodeGuardAI.Domain.Projects;
using Xunit;

namespace CodeGuardAI.UnitTests.Domain;

public sealed class ProjectTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidValues_CreatesProjectWithUtcAuditTimes()
    {
        var id = Guid.NewGuid();

        var project = Project.Create(id, "CodeGuardAI", ".", "C:/repo", UtcNow);

        Assert.Equal(id, project.Id);
        Assert.Equal("CodeGuardAI", project.Name);
        Assert.Equal(UtcNow, project.CreatedAtUtc);
        Assert.Equal(UtcNow, project.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Project.Create(Guid.Empty, "CodeGuardAI", ".", "C:/repo", UtcNow));
    }

    [Fact]
    public void Create_WithNonUtcTimestamp_Throws()
    {
        var localOffset = new DateTimeOffset(2026, 9, 29, 13, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() =>
            Project.Create(Guid.NewGuid(), "CodeGuardAI", ".", "C:/repo", localOffset));
    }
}
