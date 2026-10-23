namespace Shared.Kernel;

/// <summary>
/// A business rule refused a step inside a larger transaction (e.g. offer acceptance, ADR-006). The caller rolls the
/// transaction back and answers with <see cref="ErrorCode"/> and <see cref="Status"/>.
/// </summary>
public sealed class DomainConflictException : Exception
{
    public DomainConflictException()
        : this("conflict_state", "The operation conflicts with the current state.")
    {
    }

    public DomainConflictException(string message)
        : this("conflict_state", message)
    {
    }

    public DomainConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = "conflict_state";
    }

    public DomainConflictException(string errorCode, string message, int status = 409)
        : base(message)
    {
        ErrorCode = errorCode;
        Status = status;
    }

    public string ErrorCode { get; } = "conflict_state";

    /// <summary>The HTTP status the API answers with (409 unless stated).</summary>
    public int Status { get; } = 409;
}
