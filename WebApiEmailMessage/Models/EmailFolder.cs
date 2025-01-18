namespace WebApiEmailMessage.Models;


public class EmailFolder
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int TotalMessages { get; set; }
    public int UnreadMessages { get; set; }
}