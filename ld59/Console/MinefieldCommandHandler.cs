using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;

/// <summary>
/// Console command <c>minefield [name]</c>. With no name it opens a random board; with a name it loads
/// an authored board from <c>Content/files/minefield/&lt;name&gt;.txt</c>. <c>minefield --list</c> lists
/// the available authored boards.
/// </summary>
public class MinefieldCommandHandler : ConsoleCommandHandler
{
    private const string LevelsDir = "Content/files/minefield";

    public MinefieldCommandHandler()
    {
        CommandName = "minefield";
    }

    public override void Execute(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--list" || args[0] == "-l"))
        {
            ListLevels();
            return;
        }

        int w = 800, h = 840;
        var bounds = new Rectangle((Core.ScreenWidth - w) / 2, (Core.ScreenHeight - h) / 2, w, h);

        string levelName = args.Length > 0 ? args[0] : null;
        var ui = levelName != null ? new Minefield(bounds, levelName) : new Minefield(bounds);
        Core.UISystem.AddElement(ui);
    }

    private void ListLevels()
    {
        if (!Directory.Exists(LevelsDir))
        {
            Console.PrintLine($"No minefield levels found (directory missing: {LevelsDir})");
            return;
        }

        var files = Directory.GetFiles(LevelsDir, "*.txt")
                             .Select(Path.GetFileNameWithoutExtension)
                             .OrderBy(f => f)
                             .ToList();

        if (files.Count == 0)
        {
            Console.PrintLine("No minefield levels found.");
            return;
        }

        Console.PrintLine($"Minefield levels ({files.Count}):");
        foreach (var f in files)
            Console.PrintLine($"  {f}");
    }
}
