namespace Shared.Kernel;

public readonly record struct FeeChargeId : ITypedId<FeeChargeId>
{
    private FeeChargeId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FeeChargeId New() => new(SequentialGuid.NewGuid());

    public static FeeChargeId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
