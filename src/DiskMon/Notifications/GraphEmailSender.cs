using Com.H.GraphAPI.Identity;
using Com.H.GraphAPI.Mail;
using DiskMon.Configuration;
using Microsoft.Extensions.Logging;

namespace DiskMon.Notifications;

/// <summary>
/// Sends through the Microsoft Graph <c>sendMail</c> endpoint, using
/// <a href="https://www.nuget.org/packages/Com.H.GraphAPI">Com.H.GraphAPI</a>.
/// </summary>
/// <remarks>
/// Needs an Azure app registration with the <c>Mail.Send</c> <em>application</em> permission and
/// admin consent. No token is cached here: MSAL, underneath
/// <see cref="GIExtensions.GetAccessTokenAsync"/>, already serves repeat requests for the same
/// application from its own cache.
/// </remarks>
public sealed class GraphEmailSender(ILogger<GraphEmailSender> logger) : IEmailSender
{
    /// <inheritdoc/>
    public EmailProvider Provider => EmailProvider.Graph;

    /// <inheritdoc/>
    public async Task SendAsync(
        EmailMessage message,
        EmailSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);

        var graph = settings.Graph;

        var mail = new Message
        {
            From = settings.From,
            Subject = message.Subject,
            Body = message.HtmlBody,
            SaveToSentItems = graph.SaveToSentItems,

            // Without this, a rejection comes back as a response object the caller has to
            // inspect. An exception is what the retry-next-sweep logic upstream expects.
            ThrowOnFailure = true,

            GetAccessTokenAsyncDelegate = token => GIExtensions.GetAccessTokenAsync(
                graph.ClientId, graph.ClientSecret, graph.TenantId, token)
        };

        foreach (var address in Recipients.Split(settings.To)) mail.To.Add(address);
        foreach (var address in Recipients.Split(settings.Cc)) mail.Cc.Add(address);
        foreach (var address in Recipients.Split(settings.Bcc)) mail.Bcc.Add(address);

        logger.LogDebug(
            "Sending '{Subject}' through Graph as {From}.", message.Subject, settings.From);

        using var response = await mail
            .SendAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
