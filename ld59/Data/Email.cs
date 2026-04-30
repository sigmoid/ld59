using System.Collections.Generic;

public class Email
{
    public string FileName { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public string Subject { get; set; }
    public string Date { get; set; }
    public string Body { get; set; }
    public List<GameInfo> Info { get; set; } = new();
    public bool IsRead { get; set; } = false;
}
