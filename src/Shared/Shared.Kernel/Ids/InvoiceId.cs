namespace Shared.Kernel;

public readonly record struct InvoiceId : ITypedId<InvoiceId>
{
    private InvoiceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static InvoiceId New() => new(SequentialGuid.NewGuid());

    public static InvoiceId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
