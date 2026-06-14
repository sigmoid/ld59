using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;

public class PowergridCommandHandler : ConsoleCommandHandler
{
    private const string ScenesDir = "Content/files/scenes/powergrid";

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

        string levelName = args.Length > 0 ? args[0] : null;
        var ui = new PowergridUI(new Rectangle(150, 100, 900, 700), levelName);
        Core.UISystem.AddElement(ui);
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
