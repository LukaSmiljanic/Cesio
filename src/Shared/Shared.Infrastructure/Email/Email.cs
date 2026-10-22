using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Shared.Infrastructure.Email;

/// <summary>Sends one plain-text email (task 004; templates and channels come with Notifications, task 019).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>SMTP settings; locally smtp4dev on port 5025 (docker-compose.yml), in Azure ACS Email (task 024).</summary>
public sealed class EmailOptions
{
    public string SmtpHost { get; set; } = "localhost";

    public int SmtpPort { get; set; } = 5025;

    public bool EnableSsl { get; set; }

    public string From { get; set; } = "no-reply@cesio.local";
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.EnableSsl };
        using var mail = new MailMessage(settings.From, message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, cancellationToken);
    }
}

public static class EmailSetup
{
    public static IServiceCollection AddEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>().BindConfiguration("Email");
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
