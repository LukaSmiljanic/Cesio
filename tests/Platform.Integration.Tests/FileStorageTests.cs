using Shared.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>ADR-023: file keys stay inside their storage area.</summary>
public sealed class FileStorageTests
{
    [Theory]
    [InlineData("../clean/x")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    public void A_key_that_leaves_its_area_is_refused(string key) =>
        Assert.Throws<ArgumentException>(() => FileStorage.PathFor(Path.GetTempPath(), "documents/quarantine", key));

    [Fact]
    public void A_normal_key_stays_in_its_area() =>
        Assert.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "documents", "quarantine"), FileStorage.PathFor(Path.GetTempPath(), "documents/quarantine", "ab12cd"), StringComparison.Ordinal);
}
