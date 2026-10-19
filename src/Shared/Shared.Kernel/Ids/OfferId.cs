namespace Shared.Kernel;

public readonly record struct OfferId : ITypedId<OfferId>
{
    private OfferId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OfferId New() => new(SequentialGuid.NewGuid());

    public static OfferId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
