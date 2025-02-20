using MailKit;
using MailKit.Net.Imap;
using MimeKit;
using System.Text.Json.Serialization;
using System.Text.Json;
using WebApiEmailMessage.Models;
using Confluent.Kafka;
using System.Net.Mail;

namespace WebApiEmailMessage.Services;

public interface IEmailService
{
    Task<List<EmailMessage>> GetEmails();
    Task<List<EmailMessage>> GetEmails(int pageNumber = 1, int pageSize = 20);
    Task<List<EmailFolder>> GetAllFoldersWithDetails();
    Task<List<EmailMessage>> GetAllConsumers(string username, int pageNumber = 1, int pageSize = 20);
}

public class EmailService : IEmailService
{
    private readonly IEmailConfiguration _emailConfig;
    private readonly ProducerService _kafkaProducerService;
    private readonly ConsumerService _kafkaConsumerService;
    private readonly ConsumerConfig _consumerConfig;

    public EmailService(IEmailConfiguration emailConfiguration, ProducerService producerService, ConsumerService consumerService)
    {
        _emailConfig = emailConfiguration;
        _kafkaProducerService = producerService;
        _kafkaConsumerService = consumerService;
    }

    public async Task<List<EmailMessage>> GetEmails()
    {
        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        emailClient.Connect(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
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
                    await folder.OpenAsync(FolderAccess.ReadOnly);

                    int totalMessages = folder.Count;
                    var summaries = await folder.FetchAsync(0, -1, MessageSummaryItems.Full | MessageSummaryItems.UniqueId);
                    var sortedSummaries = summaries
                        .Where(summary => summary.Date != null)
                        .OrderByDescending(summary => summary.Date)
                        .ToList();

                    int startIndex = (pageNumber - 1) * pageSize;
                    int endIndex = Math.Min(startIndex + pageSize, sortedSummaries.Count);

                    if (startIndex < sortedSummaries.Count)
                    {
                        for (int i = startIndex; i < endIndex; i++)
                        {
                            var summary = sortedSummaries[i];
                            var item = await folder.GetMessageAsync(summary.UniqueId);

                            var emailMessage = new EmailMessage
                            {
                                UniqueId = summary.UniqueId.Id,
                                MessageId = item.MessageId,
                                Subject = item.Subject,
                                Body = string.IsNullOrEmpty(item.TextBody) ? item.HtmlBody : item.TextBody,
                                Date = item.Date,
                                IsReply = summary.IsReply,
                                IsForward = item.Subject?.ToLowerInvariant().StartsWith("fw:") == true || item.Subject?.ToLowerInvariant().StartsWith("fwd:") == true,
                                OriginalMessageId = item.Headers["In-Reply-To"]?.ToLowerInvariant(),
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
                                    string filePath = Path.Combine("attachments", fileName);
                                    Directory.CreateDirectory("attachments");
                                    using (var stream = File.Create(filePath))
                                    {
                                        await mimePart.Content.DecodeToAsync(stream);
                                    }

                                    long size = mimePart.Content.Stream?.Length ?? 0;

                                    emailMessage.Attachments.Add(new EmailAttachment
                                    {
                                        FileName = fileName,
                                        FilePath = filePath,
                                        Size = size,
                                        ContentType = mimePart.ContentType.MimeType
                                    });
                                }
                            }

                            //var emailMessage = new EmailMessage
                            //{
                            //    MessageId = $"aa {i}",
                            //    Subject = $"SS {i}",
                            //    Body = $"Hii {i}",
                            //    Date = DateTimeOffset.Now,
                            //};

                            // Publish to Kafka
                            var topic = $"email/{username}";
                            var message = JsonSerializer.Serialize(emailMessage);
                            await _kafkaProducerService.ProduceAsync(topic, message);
                        }
                    }

                    emailClient.Disconnect(true);
                }
            }
        }
        return messages;
    }

    public async Task<List<EmailMessage>> GetAllConsumers(string username, int pageNumber = 1, int pageSize = 20)
    {
        var messages = new List<EmailMessage>();
        if (string.IsNullOrWhiteSpace(username))
        {
            username = _emailConfig.ImapUsername;
        }
        var topic = $"email/{username}";

        var cancellationToken = new CancellationTokenSource().Token;
        var rawMessages = _kafkaConsumerService.ConsumeMessages(topic, (pageNumber - 1) * pageSize, pageNumber * pageSize, cancellationToken);

        foreach (var rawMessage in rawMessages)
        {
            var emailMessage = JsonSerializer.Deserialize<EmailMessage>(rawMessage);
            messages.Add(emailMessage);
        }

        return messages;
    }

    public async Task SaveSummariesToJsonAsync(IList<IMessageSummary> sortedSummaries, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        string jsonData = JsonSerializer.Serialize(sortedSummaries, options);
        await File.WriteAllTextAsync(filePath, jsonData);
        Console.WriteLine($"Summaries saved to {filePath}");
    }

    public async Task<List<string>> GetAllFolders()
    {
        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        emailClient.Connect(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
        emailClient.AuthenticationMechanisms.Remove("XOAUTH2");
        emailClient.AuthenticationMechanisms.Remove("NTLM");
        emailClient.Authenticate(username, password);

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

        var folders = new List<EmailFolder>();
        foreach (var folder in emailClient.GetFolders(emailClient.PersonalNamespaces.First()))
        {
            try
            {
                await folder.OpenAsync(FolderAccess.ReadOnly);

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
                Console.WriteLine($"Skipping folder: {folder.FullName} - {ex.Message}");
            }
        }

        emailClient.Disconnect(true);
        return folders;
    }
}
