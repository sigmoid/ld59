using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class WebPageLoader
{
    private const string WwwRoot = "Content/www/";

    public static WebPage Load(string url)
    {
        string path = WwwRoot + url;
        if (!File.Exists(path)) return null;

        string raw = File.ReadAllText(path);
        return Parse(url, raw);
    }

    private static WebPage Parse(string url, string raw)
    {
        var links = new List<(string Label, string ResolvedUrl)>();
        var display = new StringBuilder();
        var info = new List<GameInfo>();

        string normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalized.Split('\n');

        bool inInfoSection = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                inInfoSection = true;
                continue;
            }

            if (inInfoSection)
            {
                ParseInfoLine(lines[i], info);
            }
            else
            {
                display.Append(ParseLine(lines[i], url, links));
                if (i < lines.Length - 1) display.Append('\n');
            }
        }

        return new WebPage
        {
            Url = url,
            DisplayText = display.ToString().TrimEnd('\n'),
            Links = links,
            Info = info
        };
    }

    private static void ParseInfoLine(string line, List<GameInfo> info)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        int comma = line.LastIndexOf(',');
        if (comma < 0) return;
        string value = line.Substring(0, comma).Trim();
        string typeName = line.Substring(comma + 1).Trim();
        if (string.IsNullOrEmpty(value) || !Enum.TryParse<InfoType>(typeName, out var type)) return;
        info.Add(new GameInfo { Value = value, Type = type, IsUnlocked = true });
    }

    private static string ParseLine(string line, string currentUrl, List<(string, string)> links)
    {
        var result = new StringBuilder();
        int i = 0;

        while (i < line.Length)
        {
            if (line[i] == '[')
            {
                int closeLabel = line.IndexOf(']', i + 1);
                if (closeLabel > i && closeLabel + 1 < line.Length && line[closeLabel + 1] == '(')
                {
                    int closeHref = line.IndexOf(')', closeLabel + 2);
                    if (closeHref > closeLabel + 1)
                    {
                        string label = line.Substring(i + 1, closeLabel - i - 1);
                        string href = line.Substring(closeLabel + 2, closeHref - closeLabel - 2);
                        links.Add((label, ResolveUrl(currentUrl, href)));
                        result.Append(label);
                        i = closeHref + 1;
                        continue;
                    }
                }
            }
            result.Append(line[i]);
            i++;
        }

        return result.ToString();
    }

    private static string ResolveUrl(string currentUrl, string href)
    {
        if (href.StartsWith("http://") || href.StartsWith("https://"))
            return href;

        string dir = currentUrl.Contains('/')
            ? currentUrl.Substring(0, currentUrl.LastIndexOf('/') + 1)
            : "";

        var parts = (dir + href).Split('/');
        var resolved = new List<string>();
        foreach (var part in parts)
        {
            if (part == "..") { if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1); }
            else if (part != ".") resolved.Add(part);
        }
        return string.Join("/", resolved);
    }

    public static string FormatDisplayUrl(string url)
    {
        string display = url;
        if (display.EndsWith("/index.txt"))
            display = display.Substring(0, display.Length - "index.txt".Length);
        else if (display.EndsWith(".txt"))
            display = display.Substring(0, display.Length - 4);
        return "http://" + display;
    }
}
