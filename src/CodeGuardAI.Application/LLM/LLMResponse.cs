namespace CodeGuardAI.Application.LLM;

public enum LLMProviderErrorType
{
    Timeout = 1,
    RateLimit = 2,
    InvalidResponse = 3,
    Unavailable = 4
}

public sealed record LLMProviderError
{
    public LLMProviderError(
        LLMProviderErrorType type,
        string code,
        string description,
        TimeSpan? retryAfter = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (retryAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter), "Retry delay must be positive.");
        }

        Type = type;
        Code = code.Trim();
        Description = description.Trim();
        RetryAfter = retryAfter;
    }

    public LLMProviderErrorType Type { get; }

    public string Code { get; }

    public string Description { get; }

    public TimeSpan? RetryAfter { get; }
}

public sealed record LLMResponse
{
    public LLMResponse(
        string provider,
        string model,
        string promptVersion,
        string content,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration cannot be negative.");
        }

        Provider = provider.Trim();
        Model = model.Trim();
        PromptVersion = promptVersion.Trim();
        Content = content;
        Duration = duration;
    }

    public string Provider { get; }

    public string Model { get; }

    public string PromptVersion { get; }

    public string Content { get; }

    public TimeSpan Duration { get; }
}

public sealed class LLMProviderResult
{
    private readonly LLMResponse? _response;
    private readonly LLMProviderError? _error;

    private LLMProviderResult(LLMResponse response)
    {
        _response = response;
        IsSuccess = true;
    }

    private LLMProviderResult(LLMProviderError error)
    {
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public LLMResponse Response => IsSuccess
        ? _response!
        : throw new InvalidOperationException("A failed provider result does not contain a response.");

    public LLMProviderError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A successful provider result does not contain an error.");

    public static LLMProviderResult Success(LLMResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new LLMProviderResult(response);
    }

    public static LLMProviderResult Failure(LLMProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new LLMProviderResult(error);
    }
}
