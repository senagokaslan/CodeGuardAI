namespace CodeGuardAI.Infrastructure.LLM;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string? ApiKey { get; init; }

    public string BaseAddress { get; init; } = "https://generativelanguage.googleapis.com/";

    public int MaxRetries { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 100;

    public int MaxRetryJitterMilliseconds { get; init; } = 100;

    public int MaxRetryDelayMilliseconds { get; init; } = 2_000;
}
