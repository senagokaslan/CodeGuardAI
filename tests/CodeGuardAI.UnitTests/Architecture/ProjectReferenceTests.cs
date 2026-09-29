using System.Xml.Linq;
using Xunit;

namespace CodeGuardAI.UnitTests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void Production_projects_follow_the_allowed_dependency_direction()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedReferences = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CodeGuardAI.Domain"] = [],
            ["CodeGuardAI.Application"] = ["CodeGuardAI.Domain"],
            ["CodeGuardAI.Infrastructure"] = ["CodeGuardAI.Application", "CodeGuardAI.Domain"],
            ["CodeGuardAI.Api"] = ["CodeGuardAI.Application", "CodeGuardAI.Infrastructure"]
        };

        foreach (var (projectName, expected) in expectedReferences)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", projectName, $"{projectName}.csproj");
            var actual = ReadProjectReferences(projectPath);

            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
            Assert.DoesNotContain(actual, reference => reference.EndsWith("Tests", StringComparison.Ordinal));
        }
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved: {projectPath}");
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value
                ?? throw new InvalidOperationException($"ProjectReference without Include in {projectPath}."))
            .Select(reference => Path.GetFullPath(Path.Combine(projectDirectory, reference)))
            .Select(reference => Path.GetFileNameWithoutExtension(reference)
                ?? throw new InvalidOperationException($"Referenced project name could not be resolved: {reference}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodeGuardAI.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root containing CodeGuardAI.sln was not found.");
    }
}
