using System;
using System.IO;

// Player-facing solitaire stats persisted across sessions (currently just a win tally). Stored under
// the user's local app-data so it survives rebuilds and lives outside the game's content directory.
public static class SolitaireStats
{
    private static string StatsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LD59");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "solitaire.stats");
        }
    }

    public static int LoadWins()
    {
        try
        {
            if (File.Exists(StatsPath) && int.TryParse(File.ReadAllText(StatsPath).Trim(), out var wins))
                return wins;
        }
        catch { /* unreadable stats file: fall back to zero */ }
        return 0;
    }

    public static void SaveWins(int wins)
    {
        try { File.WriteAllText(StatsPath, wins.ToString()); }
        catch { /* best-effort: a failed write just loses this session's increment */ }
    }
}
