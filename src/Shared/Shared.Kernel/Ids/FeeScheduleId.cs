namespace Shared.Kernel;

public readonly record struct FeeScheduleId : ITypedId<FeeScheduleId>
{
    private FeeScheduleId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FeeScheduleId New() => new(SequentialGuid.NewGuid());

    public static FeeScheduleId From(Guid value) => new(TypedId.NotEmpty(value));

    public override string ToString() => Value.ToString();
}
