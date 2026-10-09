namespace CodeGuardAI.Application.Prompts;

public static class DocsPromptRegistry
{
    public const string CurrentVersion = "docs-v1";

    private const string InputPlaceholder = "{{DOCUMENTATION_INPUT}}";
    private static readonly Lazy<string> SystemPromptResource = new(() =>
        ReadResource("docs-system.v1.txt"));
    private static readonly Lazy<string> TaskPromptResource = new(() =>
        ReadResource("docs-task.v1.txt"));

    public static string SystemPrompt => SystemPromptResource.Value;

    public static string CreateTaskPrompt(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return TaskPromptResource.Value.Replace(InputPlaceholder, input, StringComparison.Ordinal);
    }

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(DocsPromptRegistry).Assembly;
        var resourceName = $"{typeof(DocsPromptRegistry).Namespace}.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource is unavailable: {fileName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
