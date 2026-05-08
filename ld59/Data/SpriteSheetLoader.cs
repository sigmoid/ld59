using System;
using System.IO;
using Quartz;

public static class SpriteSheetLoader
{
    private const string ContentRoot = "Content/";

    public static bool IsSpriteSheetPath(string path) =>
        path.EndsWith(".sprite", StringComparison.OrdinalIgnoreCase);

    public static SpriteSheet Load(string path)
    {
        string fullPath = ContentRoot + path;
        if (!File.Exists(fullPath)) return null;

        var sheet = new SpriteSheet();
        string imagePath = null;

        foreach (var raw in File.ReadAllLines(fullPath))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line.Substring(0, eq).Trim().ToLowerInvariant();
            var val = line.Substring(eq + 1).Trim();
            switch (key)
            {
                case "image":   imagePath = val; break;
                case "columns": if (int.TryParse(val, out int c)) sheet.Columns = Math.Max(1, c); break;
                case "rows":    if (int.TryParse(val, out int r)) sheet.Rows    = Math.Max(1, r); break;
                case "fps":     if (float.TryParse(val, out float f)) sheet.Fps = Math.Max(0.1f, f); break;
            }
        }

        if (imagePath == null) return null;

        try { sheet.Texture = Core.Content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>(imagePath); }
        catch { return null; }

        return sheet;
    }
}
