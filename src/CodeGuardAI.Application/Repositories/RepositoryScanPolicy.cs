using System.Collections.Frozen;

namespace CodeGuardAI.Application.Repositories;

public static class RepositoryScanPolicy
{
    public static readonly FrozenSet<string> AllowedExtensions = new[]
    {
        ".cs",
        ".csproj",
        ".json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> AllowedJsonFileNames = new[]
    {
        "appsettings.json",
        "appsettings.Development.json",
        "global.json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> DeniedDirectoryNames = new[]
    {
        ".git",
        "bin",
        "obj",
        "node_modules"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> SensitiveExtensions = new[]
    {
        ".key",
        ".pem",
        ".pfx",
        ".p12"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> SensitiveFileNames = new[]
    {
        ".env",
        "appsettings.Local.json",
        "credentials.json",
        "secrets.json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSensitiveFile(string fileName)
    {
        if (SensitiveFileNames.Contains(fileName) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
            SensitiveExtensions.Contains(Path.GetExtension(fileName)))
        {
            return true;
        }

        return fileName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }
}
