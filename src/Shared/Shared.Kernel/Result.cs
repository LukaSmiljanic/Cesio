namespace Shared.Kernel;

/// <summary>
/// Outcome of an operation that can fail in an expected way. Expected failures (validation, invalid state) are
/// returned as an <see cref="Kernel.Error"/>; exceptions are reserved for bugs and broken invariants.
/// </summary>
public class Result
{
    private readonly Error? _error;

    protected Result(Error? error) => _error = error;

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    /// <summary>The failure; throws when the result is a success.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

/// <summary>A <see cref="Result"/> that carries a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value; throws when the result is a failure.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"A failed result has no value ({Error.Code}).");

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory on the result type itself; also reachable via Result.Success<T>.")]
    public static Result<T> Success(T value) => new(value, null);

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory on the result type itself; also reachable via Result.Failure<T>.")]
    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}
