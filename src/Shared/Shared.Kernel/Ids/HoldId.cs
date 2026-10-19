namespace Shared.Kernel;

public readonly record struct HoldId : ITypedId<HoldId>
{
    private HoldId(Guid value) => Value = value;

    public Guid Value { get; }

    public static HoldId New() => new(SequentialGuid.NewGuid());

    public static HoldId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
