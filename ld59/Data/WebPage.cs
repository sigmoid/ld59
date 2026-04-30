using System.Collections.Generic;

public class WebPage
{
    public static readonly HashSet<string> VisitedUrls = [];

    public string Url { get; set; }
    public string DisplayText { get; set; }
    public List<(string Label, string ResolvedUrl)> Links { get; set; } = new();
    public List<GameInfo> Info { get; set; } = [];
}
