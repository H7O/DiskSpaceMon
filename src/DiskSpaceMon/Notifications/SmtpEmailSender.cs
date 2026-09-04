using DiskSpaceMon.Configuration;
using Microsoft.Extensions.Logging;

namespace DiskSpaceMon.Notifications;

/// <summary>
/// Sends through an SMTP server, using <c>Com.H.Net.Mail.Message</c> from the
/// <a href="https://www.nuget.org/packages/Com.H">Com.H</a> package.
/// </summary>
public sealed class SmtpEmailSender(ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <inheritdoc/>
    public EmailProvider Provider => EmailProvider.Smtp;

    /// <inheritdoc/>
    public async Task SendAsync(
        EmailMessage message,
        EmailSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);

        // Com.H's Message mutates itself while sending, so it is built fresh each time rather
        // than held as a field.
        using var mail = new Com.H.Net.Mail.Message
        {
            SmtpServer = settings.Smtp.Host,

            // Com.H defaults an unset port to 21, which is FTP's. Always state it.
            Port = settings.Smtp.Port,
            Ssl = settings.Smtp.EnableSsl,
            From = settings.From,
            FromDisplayName = string.IsNullOrWhiteSpace(settings.FromDisplayName)
                ? null
                : settings.FromDisplayName,
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsHtml = true
        };

        if (!string.IsNullOrWhiteSpace(settings.Smtp.Password))
        {
            // Com.H falls back to the From address when no user name is given, which is what
            // most hosted relays expect.
            mail.Uid = settings.Smtp.Username;
            mail.Pwd = settings.Smtp.Password;
        }

        foreach (var address in Recipients.Split(settings.To)) mail.To.Add(address);
        foreach (var address in Recipients.Split(settings.Cc)) mail.Cc.Add(address);
        foreach (var address in Recipients.Split(settings.Bcc)) mail.Bcc.Add(address);

        logger.LogDebug(
            "Sending '{Subject}' through {Host}:{Port}.",
            message.Subject, settings.Smtp.Host, settings.Smtp.Port);

        await mail.SendAsync(cancellationToken).ConfigureAwait(false);
    }
}
