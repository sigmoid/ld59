using Microsoft.Xna.Framework;
using Quartz;

/// <summary>
/// Console command <c>browser</c>. Opens the LithNET browser window.
/// <c>browser --list</c> lists the code-built pages registered in <see cref="WebPageRegistry"/>.
/// </summary>
public class BrowserCommandHandler : ConsoleCommandHandler
{
    public BrowserCommandHandler()
    {
        CommandName = "browser";
    }

    public override void Execute(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--list" || args[0] == "-l"))
        {
            ListPages();
            return;
        }

        int w = 900, h = 700;
        var bounds = new Rectangle((Core.ScreenWidth - w) / 2, (Core.ScreenHeight - h) / 2, w, h);
        _ = new BrowserUI(bounds);
    }

    private void ListPages()
    {
        var urls = new System.Collections.Generic.List<string>(WebPageRegistry.Urls);
        urls.Sort();

        if (urls.Count == 0)
        {
            Console.PrintLine("No code-built pages registered.");
            return;
        }

        Console.PrintLine($"Code-built pages ({urls.Count}):");
        foreach (var u in urls)
            Console.PrintLine($"  {u}");
    }
}
