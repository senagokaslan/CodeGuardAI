namespace CodeGuardAI.Application.Prompts;

public static class ReviewPromptRegistry
{
    public const string CurrentVersion = "review-v1";

    private const string ContextPlaceholder = "{{REPOSITORY_CONTEXT}}";
    private static readonly Lazy<string> SystemPromptResource = new(() =>
        ReadResource("review-system.v1.txt"));
    private static readonly Lazy<string> TaskPromptResource = new(() =>
        ReadResource("review-task.v1.txt"));

    public static string SystemPrompt => SystemPromptResource.Value;

    public static string CreateTaskPrompt(string repositoryContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryContext);
        return TaskPromptResource.Value.Replace(
            ContextPlaceholder,
            repositoryContext,
            StringComparison.Ordinal);
    }

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(ReviewPromptRegistry).Assembly;
        var resourceName = $"{typeof(ReviewPromptRegistry).Namespace}.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource is unavailable: {fileName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
