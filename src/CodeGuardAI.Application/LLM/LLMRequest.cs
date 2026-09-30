namespace CodeGuardAI.Application.LLM;

public sealed record LLMRequest
{
    public LLMRequest(
        string model,
        string promptVersion,
        TimeSpan timeout,
        string systemPrompt,
        string userPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        Model = model.Trim();
        PromptVersion = promptVersion.Trim();
        Timeout = timeout;
        SystemPrompt = systemPrompt;
        UserPrompt = userPrompt;
    }

    public string Model { get; }

    public string PromptVersion { get; }

    public TimeSpan Timeout { get; }

    public string SystemPrompt { get; }

    public string UserPrompt { get; }
}
