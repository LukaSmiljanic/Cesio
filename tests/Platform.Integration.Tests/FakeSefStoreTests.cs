using Integrations.Sef;

namespace Platform.Integration.Tests;

/// <summary>The local fake SEF keeps its invoices in a file, so the API and the worker (two processes) see the same ones.</summary>
public sealed class FakeSefStoreTests
{
    [Fact]
    public async Task An_invoice_added_by_one_process_is_seen_by_another_and_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fake-sef-{Guid.NewGuid():N}.json");
        try
        {
            var api = new FakeSefClient(path);
            var id = api.AddInvoice("key", FakeSefClient.Ubl("101134702", "106006802", "F-1", 100m, 20m), "Approved");
            var worker = new FakeSefClient(path);

            var seen = await worker.GetSalesInvoiceAsync("key", id, TestContext.Current.CancellationToken);
            worker.SetStatus("key", id, "Cancelled");
            var restarted = await new FakeSefClient(path).GetSalesInvoiceAsync("key", id, TestContext.Current.CancellationToken);

            Assert.Equal("Approved", seen?.Status);
            Assert.Equal("Cancelled", restarted?.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
