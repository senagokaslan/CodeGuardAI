namespace CodeGuard.Sample.Storage;

public sealed class ReportReader(string reportRoot)
{
    public string Read(string requestedPath)
    {
        var path = Path.Combine(reportRoot, requestedPath);
        return File.ReadAllText(path);
    }
}
