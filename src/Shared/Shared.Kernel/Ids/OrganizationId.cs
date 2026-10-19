namespace Shared.Kernel;

public readonly record struct OrganizationId : ITypedId<OrganizationId>
{
    private OrganizationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OrganizationId New() => new(SequentialGuid.NewGuid());

    public static OrganizationId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
