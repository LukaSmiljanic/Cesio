namespace Shared.Kernel;

/// <summary>A strongly typed identifier wrapping a non-empty <see cref="Guid"/>.</summary>
public interface ITypedId<TSelf>
    where TSelf : struct, ITypedId<TSelf>
{
    Guid Value { get; }

    /// <summary>A new identifier from <see cref="SequentialGuid"/>.</summary>
    static abstract TSelf New();

    /// <summary>Wraps an existing value; throws for <see cref="Guid.Empty"/>.</summary>
    static abstract TSelf From(Guid value);
}

internal static class TypedId
{
    public static Guid NotEmpty(Guid value) =>
        value != Guid.Empty ? value : throw new ArgumentException("An identifier cannot be an empty GUID.", nameof(value));
}
