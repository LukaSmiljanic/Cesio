namespace Shared.Kernel;

public readonly record struct FactorAuthorizationId : ITypedId<FactorAuthorizationId>
{
    private FactorAuthorizationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FactorAuthorizationId New() => new(SequentialGuid.NewGuid());

    public static FactorAuthorizationId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
