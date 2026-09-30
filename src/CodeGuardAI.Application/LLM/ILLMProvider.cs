namespace CodeGuardAI.Application.LLM;

public interface ILLMProvider
{
    Task<LLMProviderResult> GenerateReviewAsync(
        LLMRequest request,
        CancellationToken cancellationToken);
}
