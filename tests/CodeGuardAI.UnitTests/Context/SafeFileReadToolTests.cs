using System.Text;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using CodeGuardAI.Infrastructure.Tools;
using Xunit;

namespace CodeGuardAI.UnitTests.Context;

public sealed class SafeFileReadToolTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeGuardAI.Context",
        Guid.NewGuid().ToString("N"));

    public SafeFileReadToolTests()
    {
        Directory.CreateDirectory(_fixtureRoot);
    }

    [Fact]
    public async Task Read_accepts_strict_utf8_and_removes_utf8_bom()
    {
        var path = Path.Combine(_fixtureRoot, "source.cs");
        var content = "class Çağrı {}";
        await File.WriteAllBytesAsync(
            path,
            [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(content)]);

        var result = await CreateTool().ExecuteAsync(
            new FileReadToolInput(_fixtureRoot, "source.cs"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("source.cs", result.Value.RelativePath);
        Assert.Equal(content, result.Value.Content);
    }

    [Fact]
    public async Task Read_rejects_invalid_utf8_without_replacement()
    {
        await File.WriteAllBytesAsync(Path.Combine(_fixtureRoot, "source.cs"), [0xC3, 0x28]);

        var result = await CreateTool().ExecuteAsync(
            new FileReadToolInput(_fixtureRoot, "source.cs"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("file_read.invalid_utf8", result.ErrorCode);
    }

    [Fact]
    public async Task Read_rejects_binary_content()
    {
        await File.WriteAllBytesAsync(Path.Combine(_fixtureRoot, "source.cs"), [0x41, 0x00, 0x42]);

        var result = await CreateTool().ExecuteAsync(
            new FileReadToolInput(_fixtureRoot, "source.cs"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("file_read.binary", result.ErrorCode);
    }

    [Fact]
    public async Task Read_rejects_paths_outside_repository_without_disclosing_path()
    {
        var outsidePath = Path.Combine(Path.GetDirectoryName(_fixtureRoot)!, "outside.cs");
        await File.WriteAllTextAsync(outsidePath, "secret");

        try
        {
            var result = await CreateTool().ExecuteAsync(
                new FileReadToolInput(_fixtureRoot, Path.Combine("..", "outside.cs")),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("file_read.path_invalid", result.ErrorCode);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task Read_rejects_secret_name_even_when_file_exists()
    {
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, ".env"), "SECRET=value");

        var result = await CreateTool().ExecuteAsync(
            new FileReadToolInput(_fixtureRoot, ".env"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("file_read.path_invalid", result.ErrorCode);
    }

    private static SafeFileReadTool CreateTool()
    {
        return new SafeFileReadTool(new SafePathResolver());
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }
}
