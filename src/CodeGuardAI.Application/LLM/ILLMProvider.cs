namespace CodeGuardAI.Application.LLM;

public interface ILLMProvider
{
    string Name { get; }

    Task<LLMProviderResult> GenerateReviewAsync(
        LLMRequest request,
        CancellationToken cancellationToken);

    Task<LLMProviderResult> GenerateTestsAsync(
        LLMRequest request,
        CancellationToken cancellationToken);

    Task<LLMProviderResult> GenerateDocumentationAsync(
        LLMRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LLMProviderResult.Failure(new LLMProviderError(
            LLMProviderErrorType.Unavailable,
            "llm.documentation_not_supported",
            "The model provider does not support documentation generation.")));
    }
}
