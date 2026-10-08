namespace CodeGuard.Sample.Resources;

public sealed class LeakyFileReader
{
    public string ReadFirstLine(string path)
    {
        var reader = File.OpenText(path);
        return reader.ReadLine() ?? string.Empty;
    }
}
