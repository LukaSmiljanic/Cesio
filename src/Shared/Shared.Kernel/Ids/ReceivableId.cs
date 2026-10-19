namespace Shared.Kernel;

public readonly record struct ReceivableId : ITypedId<ReceivableId>
{
    private ReceivableId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReceivableId New() => new(SequentialGuid.NewGuid());

    public static ReceivableId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
