namespace EvalFixtures;

public sealed class ReportReader(string root)
{
    public string Read(string userPath)
    {
        var path = Path.Combine(root, userPath);
        return File.ReadAllText(path);
    }
}
