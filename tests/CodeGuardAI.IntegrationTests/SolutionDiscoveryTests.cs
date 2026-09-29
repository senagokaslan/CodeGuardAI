using Xunit;

namespace CodeGuardAI.IntegrationTests;

public sealed class SolutionDiscoveryTests
{
    [Fact]
    public void Api_composition_root_is_discoverable()
    {
        Assert.Equal("CodeGuardAI.Api", typeof(Program).Assembly.GetName().Name);
    }
}
