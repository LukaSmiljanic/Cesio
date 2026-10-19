namespace Shared.Kernel;

public readonly record struct DealId : ITypedId<DealId>
{
    private DealId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DealId New() => new(SequentialGuid.NewGuid());

    public static DealId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
