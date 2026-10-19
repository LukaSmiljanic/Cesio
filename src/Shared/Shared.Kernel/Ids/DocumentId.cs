namespace Shared.Kernel;

public readonly record struct DocumentId : ITypedId<DocumentId>
{
    private DocumentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DocumentId New() => new(SequentialGuid.NewGuid());

    public static DocumentId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
