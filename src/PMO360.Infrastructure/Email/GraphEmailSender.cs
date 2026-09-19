using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;

namespace PMO360.Infrastructure.Email;

/// <summary>
/// Sends through Microsoft Graph from the PMO's O365 mailbox, using the application permission
/// Mail.Send. Scope that permission to the one mailbox with an application access policy
/// (New-ApplicationAccessPolicy) — otherwise the app registration can send as anyone in the
/// tenant, which is far more than the BRD asks for.
/// </summary>
public sealed class GraphEmailSender(
    GraphServiceClient graph,
    IOptions<NotificationOptions> options,
    ILogger<GraphEmailSender> logger) : IEmailSender
{
    private readonly NotificationOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Notifications are switched off; '{Subject}' was not sent to {Recipients}.",
                message.Subject, string.Join(", ", message.To));
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SenderAddress))
        {
            throw new InvalidOperationException(
                "Notifications:SenderAddress is not configured; the portal has no mailbox to send from.");
        }

        // In UAT every notification is redirected to one address, so the flows can be exercised
        // end to end without reaching the business.
        var to = Recipients(message.To);
        var cc = message.Cc is null ? null : Recipients(message.Cc);

        if (to.Count == 0)
        {
            logger.LogWarning("'{Subject}' had no deliverable recipient and was not sent.", message.Subject);
            return;
        }

        var body = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = message.Subject,
                Body = new ItemBody { ContentType = BodyType.Html, Content = message.HtmlBody },
                ToRecipients = to,
                CcRecipients = cc
            },
            // The PMO mailbox keeps a copy: a notification is a record, and questions about
            // whether an escalation went out are answered from Sent Items.
            SaveToSentItems = true
        };

        await graph.Users[_options.SenderAddress].SendMail.PostAsync(body, cancellationToken: cancellationToken);
    }

    private List<Recipient> Recipients(IEnumerable<string> addresses)
    {
        if (!string.IsNullOrWhiteSpace(_options.RedirectAllTo))
        {
            return [new Recipient { EmailAddress = new EmailAddress { Address = _options.RedirectAllTo } }];
        }

        return addresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } })
            .ToList();
    }
}
