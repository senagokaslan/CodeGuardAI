using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Prompts;
using Xunit;

namespace CodeGuardAI.UnitTests.StructuredOutput;

public sealed class ReviewPromptTests
{
    [Fact]
    public void Registry_loads_separate_versioned_system_and_task_prompts()
    {
        const string context = "src/Example.cs:12: return value;";

        var taskPrompt = ReviewPromptRegistry.CreateTaskPrompt(context);

        Assert.Equal("review-v1", ReviewPromptRegistry.CurrentVersion);
        Assert.Contains("only on the repository context", ReviewPromptRegistry.SystemPrompt);
        Assert.Contains("1-based", ReviewPromptRegistry.SystemPrompt);
        Assert.DoesNotContain(context, ReviewPromptRegistry.SystemPrompt);
        Assert.Contains(context, taskPrompt);
        Assert.Contains("<repository_context>", taskPrompt);
    }

    [Fact]
    public void Prompt_version_can_be_recorded_on_provider_request()
    {
        var request = new LLMRequest(
            "review-model",
            ReviewPromptRegistry.CurrentVersion,
            TimeSpan.FromSeconds(30),
            ReviewPromptRegistry.SystemPrompt,
            ReviewPromptRegistry.CreateTaskPrompt("src/Example.cs:1: class Example {}"));

        Assert.Equal(ReviewPromptRegistry.CurrentVersion, request.PromptVersion);
    }
}
