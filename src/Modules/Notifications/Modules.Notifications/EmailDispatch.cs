using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Notifications.Contracts;
using Modules.Notifications.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence;

namespace Modules.Notifications;

/// <summary>
/// Email texts in Serbian (Latin) and English (task 019). They name only the kind of event, a deal reference where there
/// is one, and a link into the app: no amounts, no debtor or company names, no personal data in subjects.
/// </summary>
internal static class EmailTemplates
{
    private static readonly Dictionary<string, (string Sr, string En)> Titles = new()
    {
        [NotificationTypes.DealActionRequired] = ("Potrebna je vaša radnja na poslu", "Action required on a deal"),
        [NotificationTypes.FundingReported] = ("Faktor je prijavio isplatu; potvrdite prijem", "The factor reported the payment; confirm receipt"),
        [NotificationTypes.FundingConfirmed] = ("Isplata po poslu je potvrđena", "The deal payment was confirmed"),
        [NotificationTypes.DealCancelled] = ("Posao je otkazan", "A deal was cancelled"),
        [NotificationTypes.OfferReceived] = ("Stigla je nova ponuda", "A new offer arrived"),
        [NotificationTypes.OfferWithdrawn] = ("Ponuda je povučena", "An offer was withdrawn"),
        [NotificationTypes.OfferAccepted] = ("Vaša ponuda je prihvaćena", "Your offer was accepted"),
        [NotificationTypes.OfferSuperseded] = ("Vaša ponuda više nije aktivna", "Your offer is no longer active"),
        [NotificationTypes.NewListing] = ("Novi oglasi po vašim filterima", "New listings matching your filters"),
        [NotificationTypes.ReceivableVerification] = ("Provera potraživanja je završena", "A receivable check is complete"),
        [NotificationTypes.KybDecision] = ("Odluka o proveri firme", "Company review decision"),
        [NotificationTypes.FactorAuthorization] = ("Promena ovlašćenja za faktoring", "Factoring authorization changed"),
        [NotificationTypes.Hold] = ("Promena blokade", "Hold changed"),
        [NotificationTypes.SefStatusChanged] = ("SEF status fakture se promenio", "An invoice's SEF status changed"),
        [NotificationTypes.FeeStatement] = ("Izdat je obračun naknade", "A fee statement was issued"),
        [NotificationTypes.Security] = ("Bezbednosno obaveštenje", "Security notice"),
        [NotificationTypes.Reminder] = ("Podsetnik", "Reminder"),
    };

    public static EmailMessage Single(string to, string language, string type, string? reference, string url)
    {
        var (sr, en) = Titles.GetValueOrDefault(type, ("Obaveštenje", "Notification"));
        var title = language == "en" ? en : sr;
        var suffix = reference is null ? string.Empty : $" {reference}";
        var body = language == "en"
            ? $"{title}{suffix}. Open Cesio to see the details: {url}\nIf you did not expect this, contact Cesio support."
            : $"{title}{suffix}. Detalje pogledajte u aplikaciji Cesio: {url}\nAko ovo niste očekivali, javite se podršci Cesio.";
        return new EmailMessage(to, $"Cesio: {title}", body);
    }

    public static EmailMessage Digest(string to, string language, int count, string url) =>
        language == "en"
            ? new EmailMessage(to, "Cesio: new listings matching your filters", $"{count} new listing(s) match your filters: {url}")
            : new EmailMessage(to, "Cesio: novi oglasi po vašim filterima", $"Broj novih oglasa po vašim filterima: {count}. Pogledajte ih: {url}");
}

/// <summary>Sends pending notification emails with three attempts and a growing delay (1, 5 and 25 minutes); then Failed.</summary>
internal sealed class EmailDispatcher(NotificationsDbContext notifications, UnitOfWork unitOfWork, IEmailSender email, IConfiguration configuration, TimeProvider timeProvider)
{
    public const int MaxAttempts = 3;

    private static readonly int[] RetryMinutes = [1, 5, 25];

    private string BaseUrl => (configuration["Auth:PublicBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');

    /// <returns>How many emails were sent.</returns>
    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await notifications.Notifications
            .Where(n => n.EmailStatus == EmailStatus.Pending && n.NextEmailAt <= now)
            .OrderBy(n => n.NextEmailAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var notification in due)
        {
            var language = await LanguageAsync(notification.RecipientUserId, cancellationToken);
            sent += await TryAsync([notification], EmailTemplates.Single(notification.Email!, language, notification.Type, notification.Reference, BaseUrl + notification.Link), cancellationToken);
        }

        return sent;
    }

    /// <summary>One email per recipient with the new-listing notifications gathered since the last digest.</summary>
    public async Task<int> SendDigestsAsync(CancellationToken cancellationToken)
    {
        var batch = await notifications.Notifications
            .Where(n => n.EmailStatus == EmailStatus.Digest)
            .OrderBy(n => n.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var group in batch.GroupBy(n => n.RecipientKey))
        {
            var items = group.ToList();
            var language = await LanguageAsync(items[0].RecipientUserId, cancellationToken);
            sent += await TryAsync(items, EmailTemplates.Digest(items[0].Email!, language, items.Count, $"{BaseUrl}/marketplace"), cancellationToken);
        }

        return sent;
    }

    private async Task<string> LanguageAsync(Guid? userId, CancellationToken cancellationToken) =>
        userId is null
            ? "sr"
            : await notifications.UserSettings.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.Language).SingleOrDefaultAsync(cancellationToken) ?? "sr";

    private async Task<int> TryAsync(List<Notification> items, EmailMessage message, CancellationToken cancellationToken)
    {
        var ok = false;
        string? error = null;
        try
        {
            await email.SendAsync(message, cancellationToken);
            ok = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The type only: an exception message may carry the address (no PII in stored errors, security.md §8).
            error = exception.GetType().Name;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var item in items)
        {
            item.EmailAttempts++;
            if (ok)
            {
                item.EmailStatus = EmailStatus.Sent;
                item.NextEmailAt = null;
            }
            else
            {
                item.LastError = error;
                item.EmailStatus = item.EmailAttempts >= MaxAttempts ? EmailStatus.Failed : EmailStatus.Pending;
                item.NextEmailAt = item.EmailAttempts >= MaxAttempts ? null : now.AddMinutes(RetryMinutes[item.EmailAttempts - 1]);
            }
        }

        await unitOfWork.ExecuteInTransactionAsync(ct => notifications.SaveChangesAsync(ct), cancellationToken);
        return ok ? 1 : 0;
    }
}
