using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Modules.Organizations.Contracts;
using Shared.Kernel;

namespace Modules.Organizations;

/// <summary>
/// Phase-2 stub (tasks/README "Phase 2 stubs"): until payout accounts and their NBS verification exist (task 009),
/// every organization has one Verified RSD account derived from its id, so funding can be demonstrated end to end.
/// Registered only in the local, dev and staging environments.
/// </summary>
internal sealed class DevelopmentBankAccounts : IBankAccountQuery
{
    private const string BankCode = "160";

    public Task<IReadOnlyList<FundingAccount>> UsableForFundingAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FundingAccount>>([Account(organizationId)]);

    internal static FundingAccount Account(Guid organizationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"dev-account:{organizationId}"));
        var body = BankCode + (BitConverter.ToUInt64(hash) % 10_000_000_000_000UL).ToString("D13", CultureInfo.InvariantCulture);
        var control = 98 - (int)(BigInteger.Parse(body + "00", CultureInfo.InvariantCulture) % 97);
        var number = body + control.ToString("D2", CultureInfo.InvariantCulture);
        return new FundingAccount(new Guid(hash[..16]), organizationId,
            SerbianBankAccountNumber.TryParse(number, out var parsed) ? parsed : throw new InvalidOperationException("Invalid development account number."),
            "Razvojna banka (stub)", Currency.Rsd);
    }
}
