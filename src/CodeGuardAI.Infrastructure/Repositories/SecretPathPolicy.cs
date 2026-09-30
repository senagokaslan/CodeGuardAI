namespace CodeGuardAI.Infrastructure.Repositories;

public static class SecretPathPolicy
{
    private static readonly string[] SensitiveExtensions = [".key", ".pem", ".pfx", ".p12"];
    private static readonly string[] SensitiveFileNames =
        ["appsettings.Local.json", "credentials.json", "secrets.json"];
    private static readonly char[] TokenSeparators = ['.', '-', '_', ' '];

    public static bool IsDenied(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string fileName;
        try
        {
            fileName = Path.GetFileName(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
            SensitiveFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase) ||
            fileName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            SensitiveExtensions.Any(extension =>
                Path.GetExtension(fileName).Equals(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return fileName
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Any(IsSensitiveToken);
    }

    private static bool IsSensitiveToken(string token)
    {
        return token.Equals("key", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("secret", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("secrets", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("credential", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("credentials", StringComparison.OrdinalIgnoreCase);
    }
}
