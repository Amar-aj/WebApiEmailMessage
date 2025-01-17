namespace WebApiEmailMessage.Services;

public interface IEmailConfiguration
{
    string ImapServer { get; }
    int ImapPort { get; }
    string ImapUsername { get; }
    string ImapPassword { get; }
}
public class EmailConfiguration : IEmailConfiguration
{
    public string ImapServer { get; set; }

    public int ImapPort { get; set; }

    public string ImapUsername { get; set; }

    public string ImapPassword { get; set; }
}