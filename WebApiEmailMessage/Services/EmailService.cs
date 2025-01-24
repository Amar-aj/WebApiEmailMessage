using MailKit;
using MailKit.Net.Imap;
using MimeKit;
using System.Text.Json.Serialization;
using System.Text.Json;
using WebApiEmailMessage.Models;

namespace WebApiEmailMessage.Services;

public interface IEmailService
{
    Task<List<EmailMessage>> GetEmails();
    Task<List<EmailMessage>> GetEmails(int pageNumber = 1, int pageSize = 20);
    Task<List<EmailFolder>> GetAllFoldersWithDetails();
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
                    MessageId = item.MessageId,
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

        List<EmailMessage> messages = new List<EmailMessage>();
        var folders = emailClient.GetFolders(emailClient.PersonalNamespaces.First());
        if (folders.Any())
        {
            foreach (var folder in folders)
            {
                if (folder.Name == "All Mail")
                {
                    //var inbox = emailClient.Inbox;
                    await folder.OpenAsync(FolderAccess.ReadOnly); // Use ReadOnly for fetching emails


                    int totalMessages = folder.Count;

                    // Fetch all email summaries
                    var summaries = await folder.FetchAsync(0, -1, MessageSummaryItems.Full | MessageSummaryItems.UniqueId);

                    // Sort summaries by date in descending order
                    var sortedSummaries = summaries
                        .Where(summary => summary.Date != null)
                        .OrderByDescending(summary => summary.Date)
                        .ToList();

                    // Paginate
                    int startIndex = (pageNumber - 1) * pageSize;
                    int endIndex = Math.Min(startIndex + pageSize, sortedSummaries.Count);




                    // var serializableSummaries = sortedSummaries.Select(summary => new
                    // {
                    //     Subject = summary.Envelope?.Subject,
                    //     UniqueId = summary.UniqueId.ToString(),
                    //     Date = summary.Date.ToString("o"), // ISO 8601 format
                    //     From = summary.Envelope?.From?.ToString(),
                    //     To = summary.Envelope?.To?.Select(x => x.ToString()).ToList(),
                    //     Cc = summary.Envelope?.Cc?.Select(x => x.ToString()).ToList(),
                    //     Bcc = summary.Envelope?.Bcc?.Select(x => x.ToString()).ToList(),
                    //     Attachments = summary.Attachments?.Select(a => new
                    //     {
                    //         FileName = a.ContentDisposition?.FileName,
                    //         MimeType = a.ContentType?.MimeType
                    //     }).ToList()
                    // }).ToList();
                    //
                    // var options = new JsonSerializerOptions
                    // {
                    //     WriteIndented = true,
                    //     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    // };
                    //
                    // string jsonData = JsonSerializer.Serialize(serializableSummaries, options);
                    //
                    // // Optional: Save JSON to file
                    // await File.WriteAllTextAsync("email_summaries.json", jsonData);





                    // Ensure start index is within bounds
                    if (startIndex < sortedSummaries.Count)
                    {
                        for (int i = startIndex; i < endIndex; i++)
                        {
                            var summary = sortedSummaries[i];


                            var a = summary.IsReply;

                            var item = await folder.GetMessageAsync(summary.UniqueId); // Fetch the email message



                            var emailMessage = new EmailMessage
                            {
                                UniqueId = summary.UniqueId.Id,
                                MessageId = item.MessageId,
                                Subject = item.Subject,
                                Body = string.IsNullOrEmpty(item.TextBody) ? item.HtmlBody : item.TextBody,
                                Date = item.Date,
                                IsReply = summary.IsReply,
                               



                                // IsReply = !string.IsNullOrEmpty(item.Headers["In-Reply-To"]), // Check for In-Reply-To header
                                IsForward = item.Subject?.ToLowerInvariant().StartsWith("fw:") == true || item.Subject?.ToLowerInvariant().StartsWith("fwd:") == true, // Check subject prefix (case-insensitive)
                                OriginalMessageId = item.Headers["In-Reply-To"]?.ToLowerInvariant(), // Convert In-Reply-To header to lowercase
                            };


        
                            messages.Add(emailMessage);
                            emailMessage.ToAddresses.AddRange(item.To.Select(x => (MailboxAddress)x).Select(x => new EmailAddress { Address = x.Address, Name = x.Name }));
                            emailMessage.FromAddresses.AddRange(item.From.Select(x => (MailboxAddress)x).Select(x => new EmailAddress { Address = x.Address, Name = x.Name }));
                            emailMessage.CcAddresses.AddRange(item.Cc.Select(x => (MailboxAddress)x).Select(x => new EmailAddress { Address = x.Address, Name = x.Name }));
                            emailMessage.BccAddresses.AddRange(item.Bcc.Select(x => (MailboxAddress)x).Select(x => new EmailAddress { Address = x.Address, Name = x.Name }));

                            foreach (var attachment in item.Attachments)
                            {
                                if (attachment is MimePart mimePart)
                                {
                                    var fileName = mimePart.FileName;

                                    // Optional: Save the file locally
                                    string filePath = Path.Combine("attachments", fileName);
                                    Directory.CreateDirectory("attachments"); // Ensure directory exists
                                    using (var stream = File.Create(filePath))
                                    {
                                        await mimePart.Content.DecodeToAsync(stream);
                                    }

                                    // Calculate the size of the attachment
                                    long size = mimePart.Content.Stream?.Length ?? 0;

                                    emailMessage.Attachments.Add(new EmailAttachment
                                    {
                                        FileName = fileName,
                                        FilePath = filePath,
                                        Size = size, // Use calculated size
                                        ContentType = mimePart.ContentType.MimeType
                                    });
                                }
                            }
                        }
                    }

                    emailClient.Disconnect(true);
                }
            }
        }
        return messages;

    }
    public async Task SaveSummariesToJsonAsync(IList<IMessageSummary> sortedSummaries, string filePath)
    {


        // Convert to JSON
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        string jsonData = JsonSerializer.Serialize(sortedSummaries, options);

        // Write JSON to file
        await File.WriteAllTextAsync(filePath, jsonData);

        Console.WriteLine($"Summaries saved to {filePath}");
    }
    public async Task<List<string>> GetAllFolders()
    {
        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        // Connect and authenticate
        emailClient.Connect(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
        emailClient.AuthenticationMechanisms.Remove("XOAUTH2");
        emailClient.AuthenticationMechanisms.Remove("NTLM");
        emailClient.Authenticate(username, password);

        // Get the personal namespace and fetch all folders
        var folders = new List<string>();
        foreach (var folder in emailClient.GetFolders(emailClient.PersonalNamespaces.First()))
        {
            folders.Add(folder.FullName);
        }

        emailClient.Disconnect(true);
        return folders;
    }

    public async Task<List<EmailFolder>> GetAllFoldersWithDetails()
    {
        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        emailClient.Connect(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
        emailClient.AuthenticationMechanisms.Remove("XOAUTH2");
        emailClient.AuthenticationMechanisms.Remove("NTLM");
        emailClient.Authenticate(username, password);


        // Get the personal namespace and fetch all folders
        var folders = new List<EmailFolder>();
        foreach (var folder in emailClient.GetFolders(emailClient.PersonalNamespaces.First()))
        {
            try
            {
                await folder.OpenAsync(FolderAccess.ReadOnly); // Open folder in read-only mode to fetch details

                var folderDetails = new EmailFolder
                {
                    Id = folder.Id,
                    Name = folder.FullName,
                    TotalMessages = folder.Count,
                    UnreadMessages = folder.Unread,
                };

                folders.Add(folderDetails);
            }
            catch (ImapCommandException ex)
            {
                // Log the error and skip this folder
                Console.WriteLine($"Skipping folder: {folder.FullName} - {ex.Message}");
            }
        }

        emailClient.Disconnect(true);
        return folders;
    }


}