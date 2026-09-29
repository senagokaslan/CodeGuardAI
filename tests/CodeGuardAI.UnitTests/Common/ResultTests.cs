using CodeGuardAI.Application.Common;
using Xunit;

namespace CodeGuardAI.UnitTests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_contains_value_and_no_error()
    {
        var result = Result.Success("project-id");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
        Assert.Equal("project-id", result.Value);
    }

    [Fact]
    public void Failure_contains_typed_error_and_no_value()
    {
        var error = Error.NotFound("project.not_found", "Project was not found.");

        var result = Result.Failure<string>(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Failure)]
    public void Error_factory_preserves_code_description_and_type(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.Validation => Error.Validation("request.invalid", "Request is invalid."),
            ErrorType.NotFound => Error.NotFound("project.not_found", "Project was not found."),
            ErrorType.Conflict => Error.Conflict("project.conflict", "Project already exists."),
            ErrorType.Failure => Error.Failure("request.failed", "Request failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        Assert.Equal(type, error.Type);
        Assert.False(string.IsNullOrWhiteSpace(error.Code));
        Assert.False(string.IsNullOrWhiteSpace(error.Description));
    }

    [Fact]
    public void Failure_rejects_the_none_error()
    {
        var exception = Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));

        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void Error_factory_rejects_an_empty_code()
    {
        Assert.Throws<ArgumentException>(() => Error.Validation(string.Empty, "Request is invalid."));
    }
}
