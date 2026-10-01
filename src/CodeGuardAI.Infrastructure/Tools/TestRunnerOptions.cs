namespace CodeGuardAI.Infrastructure.Tools;

public sealed class TestRunnerOptions
{
    public const string SectionName = "TestRunner";
    public const int DefaultMaxOutputCharacters = 32_768;
    public const int MaximumOutputCharacters = 1_048_576;

    public string DotNetExecutablePath { get; init; } = "dotnet";

    public int MaxOutputCharacters { get; init; } = DefaultMaxOutputCharacters;

    public static bool IsValid(TestRunnerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DotNetExecutablePath) ||
            options.MaxOutputCharacters is <= 0 or > MaximumOutputCharacters)
        {
            return false;
        }

        var executable = options.DotNetExecutablePath.Trim();
        if (!Path.IsPathFullyQualified(executable))
        {
            return executable is "dotnet" or "dotnet.exe";
        }

        return Path.GetFileName(executable) is "dotnet" or "dotnet.exe" &&
               File.Exists(executable);
    }
}
