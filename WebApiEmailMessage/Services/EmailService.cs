using MailKit;
using MailKit.Net.Imap;
using MimeKit;
using WebApiEmailMessage.Models;

namespace WebApiEmailMessage.Services;

public interface IEmailService
{
    Task<List<EmailMessage>> GetEmails();
    Task<List<EmailMessage>> GetEmails(int pageNumber = 1, int pageSize = 20);
}

public class EmailService : IEmailService
{
    private readonly IEmailConfiguration _emailConfig;
    public EmailService(IEmailConfiguration emailConfiguration)
    {
        _emailConfig = emailConfiguration;
    }

    public async Task<List<EmailMessage>> GetEmails()
    {
        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        emailClient.Connect(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
        // Note: since we don't have an OAuth2 token, disable
        emailClient.AuthenticationMechanisms.Remove("XOAUTH2");
        emailClient.AuthenticationMechanisms.Remove("NTLM");
        emailClient.Authenticate(username, password);

        var inbox = emailClient.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite);
        List<EmailMessage> messages = new List<EmailMessage>();
        Console.WriteLine(inbox.Count);

        if (inbox.Count > 0)
        {

            foreach (var item in inbox)
            {
                var emailMessage = new EmailMessage
                {
                    Id = item.MessageId,
                    Subject = item.Subject,
                    Body = string.IsNullOrEmpty(item.TextBody) ? item.HtmlBody : item.TextBody,
                    Date = item.Date,
                };
                //emailMessage.ToAddresses.AddRange(item.To.Select(x => (MailboxAddress)x).Select(x => new EmailAddress { Address = x.Address, Name = x.Name }));
                //emailMessage.ToAddresses.AddRange(item.From.Select(x => (MailboxAddress)x).Select(x => new EmailAddress { Address = x.Address, Name = x.Name }));
                messages.Add(emailMessage);
            }

        }
        emailClient.Disconnect(true);
        return messages;
    }

    public async Task<List<EmailMessage>> GetEmails(int pageNumber = 1, int pageSize = 20)
    {
        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        emailClient.Connect(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
        emailClient.AuthenticationMechanisms.Remove("XOAUTH2");
        emailClient.AuthenticationMechanisms.Remove("NTLM");
        emailClient.Authenticate(username, password);

        var inbox = emailClient.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly); // Use ReadOnly for fetching emails

        List<EmailMessage> messages = new List<EmailMessage>();
        int totalMessages = inbox.Count;

        // Fetch all email summaries
        var summaries = await inbox.FetchAsync(0, -1, MessageSummaryItems.Full | MessageSummaryItems.UniqueId);

        // Sort summaries by date in descending order
        var sortedSummaries = summaries
            .Where(summary => summary.Date != null)
            .OrderByDescending(summary => summary.Date)
            .ToList();

        // Paginate
        int startIndex = (pageNumber - 1) * pageSize;
        int endIndex = Math.Min(startIndex + pageSize, sortedSummaries.Count);

        // Ensure start index is within bounds
        if (startIndex < sortedSummaries.Count)
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                var summary = sortedSummaries[i];
                var item = await inbox.GetMessageAsync(summary.UniqueId); // Fetch the email message

                var emailMessage = new EmailMessage
                {
                    Id = item.MessageId,
                    Subject = item.Subject,
                    Body = string.IsNullOrEmpty(item.TextBody) ? item.HtmlBody : item.TextBody,
                    Date = item.Date,
                };
                messages.Add(emailMessage);
            }
        }

        emailClient.Disconnect(true);
        return messages;
    }


}