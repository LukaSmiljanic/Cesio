namespace Shared.Kernel;

public readonly record struct KybCaseId : ITypedId<KybCaseId>
{
    private KybCaseId(Guid value) => Value = value;

    public Guid Value { get; }

    public static KybCaseId New() => new(SequentialGuid.NewGuid());

    public static KybCaseId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
