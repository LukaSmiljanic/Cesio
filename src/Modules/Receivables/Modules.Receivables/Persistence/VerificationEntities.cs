using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Modules.Receivables.Persistence;

/// <summary>A declaration text the seller confirms item by item (task 011). Texts come from task 000.</summary>
internal sealed class DeclarationVersion
{
    /// <summary>Placeholder until the lawyer-approved text (task 000); it blocks production (task 011).</summary>
    public const string Draft0Version = "DRAFT-0";

    /// <summary>The four declarations of product-spec.md §6.3.</summary>
    public static readonly string[] Draft0Items = ["NoPriorAssignment", "NoAssignmentProhibition", "NoSetOffOrDispute", "GoodsOrServicesDelivered"];

    private const string Draft0Text =
        "NACRT (DRAFT-0), čeka konačan tekst. Potvrđujem: (1) potraživanje nije ranije ustupljeno niti založeno; "
        + "(2) ugovor sa dužnikom ne zabranjuje ustupanje; (3) ne postoji prebijanje ni spor sa dužnikom; "
        + "(4) roba je isporučena odnosno usluga izvršena.";

    public static readonly DeclarationVersion Draft0 = new()
    {
        Version = Draft0Version,
        Text = Draft0Text,
        ItemCodes = JsonSerializer.Serialize(Draft0Items),
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Draft0Text))),
        CreatedAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero),
    };

    public required string Version { get; init; }

    public required string Text { get; init; }

    /// <summary>JSON array of the item codes, each answered yes or no.</summary>
    public required string ItemCodes { get; init; }

    public required string Sha256 { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The seller's answers to one declaration version. Append-only: a correction is a new row.</summary>
internal sealed class SellerDeclaration
{
    public Guid Id { get; init; }

    public Guid ReceivableId { get; init; }

    public required string Version { get; init; }

    /// <summary>JSON object item code → true/false.</summary>
    public required string Answers { get; init; }

    public bool AllConfirmed { get; init; }

    public Guid AcceptedBy { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }

    public string? IpAddress { get; init; }
}

/// <summary>A supporting document (contract, order, delivery proof) attached to a receivable.</summary>
internal sealed class ReceivableDocument
{
    public Guid ReceivableId { get; init; }

    public Guid DocumentId { get; init; }

    public Guid AttachedBy { get; init; }

    public DateTimeOffset AttachedAt { get; init; }
}

internal enum CheckResult
{
    Pass,
    Warn,
    Fail,
}

/// <summary>One check of one verification run. Append-only: re-verification adds a new run (task 011).</summary>
internal sealed class VerificationCheck
{
    public Guid Id { get; init; }

    public Guid ReceivableId { get; init; }

    public int Run { get; init; }

    public required string Code { get; init; }

    public CheckResult Result { get; init; }

    public string? Details { get; init; }

    public DateTimeOffset RanAt { get; init; }
}
