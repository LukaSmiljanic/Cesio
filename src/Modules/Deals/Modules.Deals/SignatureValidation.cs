using System.Text;
using System.Text.Json;

namespace Modules.Deals;

/// <summary>
/// One PAdES signature found in an uploaded PDF, as the validator judged it (task 016 requirement 3). The signer is a
/// person (ADR-020); the platform matches the name to the organization's legal representatives.
/// </summary>
/// <param name="SignedRevisionSha256">SHA-256 of the original revision the signature covers (incremental updates allowed).</param>
/// <param name="ReportJson">The validator's full report, stored as evidence.</param>
internal sealed record SignatureCheck(
    string SignerName,
    string CertificateSubject,
    string CertificateIssuer,
    string CertificateSerial,
    DateTimeOffset? SigningTime,
    bool IntegrityValid,
    bool Qualified,
    bool ChainValid,
    bool NotRevoked,
    string? SignedRevisionSha256,
    string ReportJson);

internal enum ValidationOutcome
{
    Validated,

    /// <summary>The validator or a revocation source could not be reached: fail closed, retry later.</summary>
    Unavailable,
}

internal sealed record SignatureValidationResult(ValidationOutcome Outcome, IReadOnlyList<SignatureCheck> Signatures);

/// <summary>PAdES/QES validation (integrations.md §7). The production implementation is chosen by the spike of ADR-011.</summary>
internal interface ISignatureValidator
{
    Task<SignatureValidationResult> ValidateAsync(byte[] pdf, CancellationToken cancellationToken);
}

/// <summary>
/// Outside development no validator is chosen yet (ADR-011 is Proposed): every validation is Unavailable, so no deal
/// can become Signed until a real validator is configured.
/// </summary>
internal sealed class UnconfiguredSignatureValidator : ISignatureValidator
{
    public Task<SignatureValidationResult> ValidateAsync(byte[] pdf, CancellationToken cancellationToken) =>
        Task.FromResult(new SignatureValidationResult(ValidationOutcome.Unavailable, []));
}

/// <summary>
/// Development and test validator: reads <c>%CESIO-FAKE-SIGNATURE {json}</c> lines from the PDF instead of real
/// PAdES signatures. Registered only with <c>Signatures:Validator=Fake</c> in local environments (like the fake SEF).
/// </summary>
internal sealed class FakeSignatureValidator : ISignatureValidator
{
    public const string Marker = "%CESIO-FAKE-SIGNATURE ";

    public const string UnavailableMarker = "%CESIO-FAKE-VALIDATOR-UNAVAILABLE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SignatureValidationResult> ValidateAsync(byte[] pdf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var text = Encoding.Latin1.GetString(pdf);
        if (text.Contains(UnavailableMarker, StringComparison.Ordinal))
        {
            return Task.FromResult(new SignatureValidationResult(ValidationOutcome.Unavailable, []));
        }

        var signatures = new List<SignatureCheck>();
        foreach (var line in text.Split('\n'))
        {
            if (!line.StartsWith(Marker, StringComparison.Ordinal))
            {
                continue;
            }

            var json = line[Marker.Length..].Trim();
            var fake = JsonSerializer.Deserialize<FakeSignature>(json, Json);
            if (fake?.Signer is null)
            {
                continue;
            }

            signatures.Add(new SignatureCheck(
                fake.Signer,
                $"CN={fake.Signer}",
                fake.Issuer ?? "CN=Fake Qualified CA",
                fake.Serial ?? "01",
                fake.SigningTime,
                fake.Integrity ?? true,
                fake.Qualified ?? true,
                fake.ChainValid ?? true,
                !(fake.Revoked ?? false),
                fake.Covers,
                json));
        }

        return Task.FromResult(new SignatureValidationResult(ValidationOutcome.Validated, signatures));
    }

    private sealed record FakeSignature(
        string? Signer,
        string? Covers,
        bool? Integrity,
        bool? Qualified,
        bool? ChainValid,
        bool? Revoked,
        string? Issuer,
        string? Serial,
        DateTimeOffset? SigningTime);
}
