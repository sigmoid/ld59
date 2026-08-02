using Quartz;
using ld59.UI;

/// <summary>
/// Console command <c>depthview</c>: toggles the 3D depth-pass debug overlay. Shows the scene's
/// linear depth buffer as a picture-in-picture (dark = close, bright = at the depth far distance)
/// plus the world distance sampled at the crosshair -- so the depth pass can be verified on its
/// own, whether or not anything is consuming it. Also forces the pass to run while it's on.
/// Optional argument sets the depth range: <c>depthview 500</c>.
/// </summary>
public class DepthViewCommandHandler : ConsoleCommandHandler
{
    public DepthViewCommandHandler()
    {
        CommandName = "depthview";
    }

    public override void Execute(string[] args)
    {
        if (args != null && args.Length >= 1 && float.TryParse(args[0], out float far) && far > 0f)
        {
            foreach (var view in UI3DScene.Instances)
                view.DepthFarDistance = far;
            UI3DScene.DebugDepthView = true;
            Console.PrintLine($"depth view: on, range {far:0.##} units");
            return;
        }

        UI3DScene.DebugDepthView = !UI3DScene.DebugDepthView;
        if (UI3DScene.Instances.Count == 0)
            Console.PrintLine($"depth view: {(UI3DScene.DebugDepthView ? "on" : "off")} (no 3D view open)");
        else
            Console.PrintLine($"depth view: {(UI3DScene.DebugDepthView ? "on" : "off")}, " +
                              $"range {UI3DScene.Instances[0].DepthFarDistance:0.##} units");
    }
}
