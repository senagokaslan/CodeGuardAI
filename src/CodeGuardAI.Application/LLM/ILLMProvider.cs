namespace CodeGuardAI.Application.LLM;

public interface ILLMProvider
{
    string Name { get; }

    Task<LLMProviderResult> GenerateReviewAsync(
        LLMRequest request,
        CancellationToken cancellationToken);
}
