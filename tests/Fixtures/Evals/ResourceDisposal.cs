namespace EvalFixtures;

public sealed class DocumentReader
{
    public string Read(string path)
    {
        var stream = File.OpenRead(path);
        return new StreamReader(stream).ReadToEnd();
    }
}
