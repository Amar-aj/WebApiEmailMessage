namespace WebApiEmailMessage.Models;

public class EmailMessage
{
    public EmailMessage()
    {
        ToAddresses = new List<EmailAddress>();
        FromAddresses = new List<EmailAddress>();
        CcAddresses = new List<EmailAddress>();
        BccAddresses = new List<EmailAddress>();
        Attachments = new List<EmailAttachment>();
    }

    public string Id { get; set; }
    public List<EmailAddress> ToAddresses { get; set; }
    public List<EmailAddress> FromAddresses { get; set; }
    public List<EmailAddress> CcAddresses { get; set; }
    public List<EmailAddress> BccAddresses { get; set; }
    public List<EmailAttachment> Attachments { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public DateTimeOffset Date { get; set; }
}
