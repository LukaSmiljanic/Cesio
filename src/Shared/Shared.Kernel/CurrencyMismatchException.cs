namespace Shared.Kernel;

/// <summary>Thrown when an operation combines amounts in different currencies (ADR-008).</summary>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException()
    {
    }

    public CurrencyMismatchException(string message)
        : base(message)
    {
    }

    public CurrencyMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine {left.Code} and {right.Code} amounts.")
    {
    }
}
