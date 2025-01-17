﻿namespace WebApiEmailMessage.Models;

public class EmailMessage
{
    public EmailMessage()
    {
        ToAddresses = new List<EmailAddress>();
        FromAddresses = new List<EmailAddress>();
    }
    public string Id { get; set; }
    public List<EmailAddress> ToAddresses { get; set; }
    public List<EmailAddress> FromAddresses { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public DateTimeOffset Date { get; set; }
}