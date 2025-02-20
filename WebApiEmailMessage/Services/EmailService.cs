using MailKit;
using MailKit.Net.Imap;
using MimeKit;
using System.Text.Json.Serialization;
using System.Text.Json;
using WebApiEmailMessage.Models;
using Confluent.Kafka;
using System.Net.Mail;
using System;

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
        //emailClient.Capabilities.HasFlag(ImapCapabilities.Pipelining);
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
        var messages = new List<EmailMessage>();

        using var emailClient = new ImapClient();
        var username = _emailConfig.ImapUsername;
        var password = _emailConfig.ImapPassword;

        await emailClient.ConnectAsync(_emailConfig.ImapServer, _emailConfig.ImapPort, true);
        emailClient.AuthenticationMechanisms.Remove("XOAUTH2");
        emailClient.AuthenticationMechanisms.Remove("NTLM");
        await emailClient.AuthenticateAsync(username, password);

        var folders = await emailClient.GetFoldersAsync(emailClient.PersonalNamespaces.First());
        var tasks = folders.Where(folder => folder.Name == "All Mail").Select(folder => ProcessFolderAsync(emailClient, folder, pageNumber, pageSize, username)).ToList();

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
        {
            messages.AddRange(result);
        }

        await emailClient.DisconnectAsync(true);
        return messages;
    }

    private async Task<List<EmailMessage>> ProcessFolderAsync(ImapClient emailClient, IMailFolder folder, int pageNumber, int pageSize, string username)
    {
        var messages = new List<EmailMessage>();

        lock (emailClient.SyncRoot)
        {
            folder.Open(FolderAccess.ReadOnly);
        }

        int totalMessages = folder.Count;
        int startIndex = (pageNumber - 1) * pageSize;
        int endIndex = Math.Min(startIndex + pageSize, totalMessages - 1);

        if (startIndex < totalMessages)
        {
            IList<IMessageSummary> summaries;
            lock (emailClient.SyncRoot)
            {
                summaries = folder.Fetch(startIndex, endIndex, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope);
            }

            var tasks = summaries.Select(summary => GetEmailMessageAsync(emailClient, folder, summary)).ToList();
            var emailMessages = await Task.WhenAll(tasks);
            messages.AddRange(emailMessages);

            // Batch Kafka messages
            //var topic = $"email/{username}";
            //var kafkaMessages = emailMessages.Select(emailMessage => JsonSerializer.Serialize(emailMessage)).ToList();
            //await _kafkaProducerService.ProduceBatchAsync(topic, kafkaMessages);
        }

        return messages;
    }

    private async Task<EmailMessage> GetEmailMessageAsync(ImapClient emailClient, IMailFolder folder, IMessageSummary summary)
    {
        MimeMessage item;
        lock (emailClient.SyncRoot)
        {
            item = folder.GetMessage(summary.UniqueId);
        }

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

        return emailMessage;
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
