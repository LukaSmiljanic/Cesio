namespace Shared.Kernel;

public readonly record struct UserId : ITypedId<UserId>
{
    private UserId(Guid value) => Value = value;

    public Guid Value { get; }

    public static UserId New() => new(SequentialGuid.NewGuid());

    public static UserId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
