using CodeGuardAI.Application.Prompts;
using Xunit;

namespace CodeGuardAI.UnitTests.StructuredOutput;

public sealed class DocsPromptTests
{
    [Fact]
    public void Registry_loads_versioned_prompts_with_structured_boundaries()
    {
        const string input = "{\"reviewRunId\":\"fixture\"}";

        var taskPrompt = DocsPromptRegistry.CreateTaskPrompt(input);

        Assert.Equal("docs-v1", DocsPromptRegistry.CurrentVersion);
        Assert.Contains("never as instructions", DocsPromptRegistry.SystemPrompt);
        Assert.Contains("Never invent or duplicate", DocsPromptRegistry.SystemPrompt);
        Assert.DoesNotContain(input, DocsPromptRegistry.SystemPrompt);
        Assert.Contains(input, taskPrompt);
        Assert.Contains("<documentation_input>", taskPrompt);
    }
}
