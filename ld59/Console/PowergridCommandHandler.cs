using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;

public class PowergridCommandHandler : ConsoleCommandHandler
{
    private const string ScenesDir = "Content/files/scenes/powergrid";
    private const string ProgressionsDir = "Content/files/scenes/powergrid/progressions";

    public PowergridCommandHandler()
    {
        CommandName = "powergrid";
    }

    public override void Execute(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--list" || args[0] == "-l"))
        {
            ListLevels();
            return;
        }

        if (args.Length > 0 && (args[0] == "--copy" || args[0] == "-c"))
        {
            if (args.Length < 3)
            {
                Console.PrintLine("Usage: powergrid --copy <source> <newName>");
                return;
            }
            CopyLevel(args[1], args[2]);
            return;
        }

        if (args.Length > 0 && (args[0] == "--progs"))
        {
            ListProgressions();
            return;
        }

        if (args.Length > 0 && (args[0] == "--prog" || args[0] == "-p"))
        {
            if (args.Length < 2)
            {
                Console.PrintLine("Usage: powergrid --prog <progressionName>");
                return;
            }
            OpenProgression(args[1]);
            return;
        }

        string levelName = args.Length > 0 ? args[0] : null;
        var ui = new PowergridUI(new Rectangle(40, 70, 1150, 720), levelName);
        Core.UISystem.AddElement(ui);
    }

    /// <summary>Opens a progression file: a text file under <see cref="ProgressionsDir"/> listing one
    /// level name per line (blank lines and lines starting with '#' are ignored).</summary>
    private void OpenProgression(string name)
    {
        var levels = LoadProgression(name);
        if (levels.Count == 0)
        {
            Console.PrintLine($"powergrid --prog: '{name}' lists no valid levels");
            return;
        }

        var ui = new PowergridUI(new Rectangle(40, 70, 1150, 720), name, levels);
        Core.UISystem.AddElement(ui);
        Console.PrintLine($"Opened progression '{name}' ({levels.Count} levels)");
    }

    /// <summary>Reads a progression file (one level name per line; blank lines and '#' comments ignored)
    /// and returns the ordered list of levels whose scene files actually exist. Returns an empty list if
    /// the progression file is missing. Shared by the console command and the Start-menu launcher.</summary>
    public static List<string> LoadProgression(string name)
    {
        var levels = new List<string>();
        var path = $"{ProgressionsDir}/{name}.txt";
        if (!File.Exists(path))
        {
            Core.DeveloperConsole?.PrintLine($"powergrid: progression '{name}' not found ({path})");
            return levels;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            if (File.Exists($"{ScenesDir}/{line}.xml"))
                levels.Add(line);
            else
                Core.DeveloperConsole?.PrintLine($"powergrid: skipping missing level '{line}'");
        }

        return levels;
    }

    private void ListProgressions()
    {
        if (!Directory.Exists(ProgressionsDir))
        {
            Console.PrintLine($"No progressions found (directory missing: {ProgressionsDir})");
            return;
        }

        var files = Directory.GetFiles(ProgressionsDir, "*.txt")
                             .Select(Path.GetFileNameWithoutExtension)
                             .OrderBy(f => f)
                             .ToList();

        if (files.Count == 0)
        {
            Console.PrintLine("No progressions found.");
            return;
        }

        Console.PrintLine($"Powergrid progressions ({files.Count}):");
        foreach (var f in files)
            Console.PrintLine($"  {f}");
    }

    private void CopyLevel(string source, string dest)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest))
        {
            Console.PrintLine("powergrid --copy: source and new name must be non-empty");
            return;
        }

        var sourcePath = $"{ScenesDir}/{source}.xml";
        var destPath = $"{ScenesDir}/{dest}.xml";

        if (!File.Exists(sourcePath))
        {
            Console.PrintLine($"powergrid --copy: source level '{source}' not found");
            return;
        }
        if (File.Exists(destPath))
        {
            Console.PrintLine($"powergrid --copy: '{dest}' already exists — choose another name");
            return;
        }

        File.Copy(sourcePath, destPath);
        Console.PrintLine($"Copied '{source}' -> '{dest}'");
    }

    private void ListLevels()
    {
        if (!Directory.Exists(ScenesDir))
        {
            Console.PrintLine($"No powergrid levels found (directory missing: {ScenesDir})");
            return;
        }

        var files = Directory.GetFiles(ScenesDir, "*.xml")
                             .Select(f => Path.GetFileNameWithoutExtension(f))
                             .OrderBy(f => f)
                             .ToList();

        if (files.Count == 0)
        {
            Console.PrintLine("No powergrid levels found.");
            return;
        }

        Console.PrintLine($"Powergrid levels ({files.Count}):");
        foreach (var f in files)
            Console.PrintLine($"  {f}");
    }
}
