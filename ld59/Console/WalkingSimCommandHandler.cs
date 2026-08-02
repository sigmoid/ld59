using Quartz;
using ld59.UI;

/// <summary>
/// Console command <c>walkingsim [file]</c>: opens the walking sim directly, without going through
/// the start menu. Defaults to <c>empty_level.scene3d</c>. Same shortcut the other apps have
/// (<c>minefield</c>, <c>browser</c>, <c>powergrid</c>) -- handy when iterating on the 3D view,
/// since reaching it otherwise takes a menu trip every launch.
/// </summary>
public class WalkingSimCommandHandler : ConsoleCommandHandler
{
    private const string DefaultScene = "empty_level.scene3d";

    public WalkingSimCommandHandler()
    {
        CommandName = "walkingsim";
    }

    public override void Execute(string[] args)
    {
        string path = args != null && args.Length > 0 ? args[0] : DefaultScene;

        var file = Core.CurrentScene.GetManager<GameFileDataManager>()?.GetFileByPath(path);
        if (file == null)
        {
            Console.PrintLine($"walkingsim: no such file '{path}'");
            return;
        }

        Core.UISystem.AddElement(new WalkingSimUI(file));
        Console.PrintLine($"walkingsim: opened {path}");
    }
}
