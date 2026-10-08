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
            ["CodeGuardAI.Api"] = ["CodeGuardAI.Application", "CodeGuardAI.Infrastructure"],
            ["CodeGuardAI.McpHost"] = ["CodeGuardAI.Application", "CodeGuardAI.Infrastructure"]
        };

        foreach (var (projectName, expected) in expectedReferences)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", projectName, $"{projectName}.csproj");
            var actual = ReadProjectReferences(projectPath);

            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
            Assert.DoesNotContain(actual, reference => reference.EndsWith("Tests", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Mcp_sdk_is_isolated_to_the_mcp_host()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionProjects = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        var projectsWithMcpPackage = productionProjects
            .Where(project => XDocument.Load(project)
                .Descendants("PackageReference")
                .Any(reference => (reference.Attribute("Include")?.Value ?? string.Empty)
                    .StartsWith("ModelContextProtocol", StringComparison.Ordinal)))
            .Select(project => Path.GetFileNameWithoutExtension(project)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["CodeGuardAI.McpHost"], projectsWithMcpPackage);
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
            .Select(reference => reference
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar))
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
