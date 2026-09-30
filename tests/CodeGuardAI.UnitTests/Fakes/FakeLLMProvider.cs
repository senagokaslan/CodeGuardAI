using CodeGuardAI.Application.LLM;

namespace CodeGuardAI.UnitTests.Fakes;

public sealed class FakeLLMProvider : ILLMProvider
{
    private readonly Queue<Func<LLMRequest, CancellationToken, Task<LLMProviderResult>>> _steps = [];
    private readonly List<LLMInvocation> _invocations = [];

    public IReadOnlyList<LLMInvocation> Invocations => _invocations;

    public void EnqueueResult(LLMProviderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _steps.Enqueue((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        });
    }

    public void Enqueue(
        Func<LLMRequest, CancellationToken, Task<LLMProviderResult>> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Enqueue(step);
    }

    public Task<LLMProviderResult> GenerateReviewAsync(
        LLMRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("No fake LLM response is queued.");
        }

        _invocations.Add(new LLMInvocation(request, cancellationToken));
        return _steps.Dequeue()(request, cancellationToken);
    }
}

public sealed record LLMInvocation(
    LLMRequest Request,
    CancellationToken CancellationToken);
