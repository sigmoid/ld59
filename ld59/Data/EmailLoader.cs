using System;
using System.Collections.Generic;
using System.IO;

public class EmailLoader
{
    public List<Email> LoadAll(string folderPath)
    {
        var emails = new List<Email>();
        if (!Directory.Exists(folderPath)) return emails;

        foreach (var file in Directory.GetFiles(folderPath, "*.eml"))
        {
            var email = Load(file);
            if (email != null) emails.Add(email);
        }
        return emails;
    }

    public Email Load(string path)
    {
        if (!File.Exists(path)) return null;

        var content = File.ReadAllText(path);
        var sections = content.Split("---");

        var email = new Email { FileName = Path.GetFileName(path) };

        var lines = sections[0].Split('\n');
        bool inHeaders = true;
        var bodyLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (inHeaders)
            {
                if (string.IsNullOrEmpty(line))
                {
                    inHeaders = false;
                    continue;
                }

                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    var key = line.Substring(0, colonIdx).Trim().ToLower();
                    var value = line.Substring(colonIdx + 1).Trim();
                    switch (key)
                    {
                        case "from":    email.From    = value; break;
                        case "to":      email.To      = value; break;
                        case "subject": email.Subject = value; break;
                        case "date":    email.Date    = value; break;
                    }
                }
            }
            else
            {
                bodyLines.Add(line);
            }
        }

        email.Body = string.Join("\n", bodyLines).Trim();

        if (sections.Length > 1)
            email.Info.AddRange(ParseInfoUnlocks(sections[1]));

        return email;
    }

    private List<GameInfo> ParseInfoUnlocks(string data)
    {
        var unlocks = new List<GameInfo>();
        foreach (var line in data.Split('\n'))
        {
            if (line.TrimStart().StartsWith("#")) continue;
            var parts = line.Split(',');
            if (parts.Length == 2)
            {
                var value = parts[0].Trim();
                if (string.IsNullOrEmpty(value)) continue;
                if (Enum.TryParse<InfoType>(parts[1].Trim(), out var type))
                    unlocks.Add(new GameInfo { Value = value, Type = type });
            }
        }
        return unlocks;
    }
}
