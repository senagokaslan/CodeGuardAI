using CodeGuardAI.Infrastructure.Repositories;
using Xunit;

namespace CodeGuardAI.UnitTests.RepositorySecurity;

public sealed class SecretPathPolicyTests
{
    [Theory]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("private.key")]
    [InlineData("certificate.pem")]
    [InlineData("api-key.cs")]
    [InlineData("service_secrets.json")]
    [InlineData("clientsecret.cs")]
    [InlineData("cloud-credential.cs")]
    [InlineData("credentials.json")]
    [InlineData("appsettings.Local.json")]
    public void Sensitive_file_names_are_denied(string fileName)
    {
        Assert.True(SecretPathPolicy.IsDenied(fileName));
    }

    [Theory]
    [InlineData("monkey.cs")]
    [InlineData("keyboard.cs")]
    [InlineData("appsettings.json")]
    [InlineData("source.cs")]
    public void Non_secret_file_names_are_not_denied(string fileName)
    {
        Assert.False(SecretPathPolicy.IsDenied(fileName));
    }
}
