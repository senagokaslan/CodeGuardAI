namespace EvalFixtures;

public sealed class SafeReportReader(string root)
{
    public string Read(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            throw new ArgumentException("A relative file name is required.", nameof(fileName));
        }

        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, fileName));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The path leaves the report root.");
        }

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
