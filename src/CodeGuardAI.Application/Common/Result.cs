namespace CodeGuardAI.Application.Common;

public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Failure = 4
}

public sealed record Error
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    private Error(string code, string description, ErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    public string Code { get; }

    public string Description { get; }

    public ErrorType Type { get; }

    public static Error Validation(string code, string description) =>
        Create(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) =>
        Create(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) =>
        Create(code, description, ErrorType.Conflict);

    public static Error Failure(string code, string description) =>
        Create(code, description, ErrorType.Failure);

    private static Error Create(string code, string description, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Error(code, description, type);
    }
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot contain an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must contain an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) where T : notnull => new(value);

    public static Result<T> Failure<T>(Error error) where T : notnull => new(error);
}

public sealed class Result<T> : Result where T : notnull
{
    private readonly T? _value;

    internal Result(T value)
        : base(true, Error.None)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    internal Result(Error error)
        : base(false, error)
    {
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result does not contain a value.");
}
