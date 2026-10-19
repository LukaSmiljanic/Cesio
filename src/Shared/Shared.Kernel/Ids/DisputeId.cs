namespace Shared.Kernel;

public readonly record struct DisputeId : ITypedId<DisputeId>
{
    private DisputeId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DisputeId New() => new(SequentialGuid.NewGuid());

    public static DisputeId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
