namespace WebApiEmailMessage.Models;

public class EmailAttachment
{
    public string FileName { get; set; }
    public string FilePath { get; set; } // Path where the file is saved (optional)
    public long Size { get; set; }       // Size in bytes
    public string ContentType { get; set; } // MIME type of the attachment
}