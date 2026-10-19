namespace Shared.Kernel;

/// <summary>
/// An expected failure with a stable snake_case code (api.md §1, e.g. <c>conflict_state</c>) and a message for
/// developers. The code is what clients and the ProblemDetails <c>errorCode</c> rely on.
/// </summary>
public sealed record Error
{
    private const int MaxCodeLength = 64;

    public Error(string code, string message)
    {
        if (!IsSnakeCase(code))
        {
            throw new ArgumentException("Error codes are non-empty snake_case (a-z, 0-9, _), starting with a letter.", nameof(code));
        }

        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }

    private static bool IsSnakeCase(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength || !char.IsAsciiLetterLower(code[0]))
        {
            return false;
        }

        foreach (var c in code)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
