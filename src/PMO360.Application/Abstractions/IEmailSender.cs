namespace PMO360.Application.Abstractions;

public sealed record EmailMessage(
    IReadOnlyCollection<string> To,
    string Subject,
    string HtmlBody,
    IReadOnlyCollection<string>? Cc = null);

/// <summary>Outbound mail. Implemented over Microsoft Graph (sendMail) against an O365 mailbox.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
