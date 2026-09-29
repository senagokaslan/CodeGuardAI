using System.Xml.Linq;
using Xunit;

namespace CodeGuardAI.UnitTests.Architecture;

public sealed class PersistencePackageBoundaryTests
{
    [Fact]
    public void Application_does_not_reference_ef_core_or_npgsql_packages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "CodeGuardAI.Application",
            "CodeGuardAI.Application.csproj");
        var packageNames = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(packageNames, name =>
            name.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));
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
