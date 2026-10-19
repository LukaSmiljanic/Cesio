using System.Data.SqlTypes;

namespace Shared.Kernel.Tests;

public sealed class TypedIdTests
{
    [Fact]
    public void Sequential_guids_ascend_in_sql_server_order()
    {
        var generated = Enumerable.Range(0, 10_000).Select(_ => SequentialGuid.NewGuid()).ToList();

        var sortedLikeSqlServer = generated.OrderBy(guid => new SqlGuid(guid)).ToList();

        Assert.Equal(generated, sortedLikeSqlServer);
        Assert.Equal(generated.Count, generated.Distinct().Count());
    }

    [Fact]
    public void Every_typed_id_wraps_a_non_empty_guid()
    {
        AssertTypedId<UserId>();
        AssertTypedId<OrganizationId>();
        AssertTypedId<KybCaseId>();
        AssertTypedId<FactorAuthorizationId>();
        AssertTypedId<HoldId>();
        AssertTypedId<DebtorId>();
        AssertTypedId<InvoiceId>();
        AssertTypedId<ReceivableId>();
        AssertTypedId<ListingId>();
        AssertTypedId<OfferId>();
        AssertTypedId<DealId>();
        AssertTypedId<DisputeId>();
        AssertTypedId<FeeScheduleId>();
        AssertTypedId<FeeChargeId>();
        AssertTypedId<DocumentId>();
    }

    private static void AssertTypedId<T>()
        where T : struct, ITypedId<T>
    {
        var id = T.New();
        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id, T.From(id.Value));
        Assert.Equal(id.Value.ToString(), id.ToString());
        Assert.Throws<ArgumentException>(() => T.From(Guid.Empty));
    }
}
