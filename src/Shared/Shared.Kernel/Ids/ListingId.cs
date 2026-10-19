namespace Shared.Kernel;

public readonly record struct ListingId : ITypedId<ListingId>
{
    private ListingId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ListingId New() => new(SequentialGuid.NewGuid());

    public static ListingId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
