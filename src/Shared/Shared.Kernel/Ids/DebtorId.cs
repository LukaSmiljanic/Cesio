namespace Shared.Kernel;

public readonly record struct DebtorId : ITypedId<DebtorId>
{
    private DebtorId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DebtorId New() => new(SequentialGuid.NewGuid());

    public static DebtorId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
