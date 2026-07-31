using Quartz;
using ld59.UI;

/// <summary>
/// Console command <c>idview</c>: toggles the walking-sim ID-buffer debug overlay. Shows the pick
/// buffer as a picture-in-picture (grey = plain mesh, green = interactable, yellow = hovered) plus
/// the id sampled at the crosshair, so you can see whether an object is rendered into the buffer
/// and recognised as interactable. Works even without mouse capture.
/// </summary>
public class IdViewCommandHandler : ConsoleCommandHandler
{
    public IdViewCommandHandler()
    {
        CommandName = "idview";
    }

    public override void Execute(string[] args)
    {
        UI3DScene.DebugIdView = !UI3DScene.DebugIdView;
        Console.PrintLine($"id-buffer debug view: {(UI3DScene.DebugIdView ? "on" : "off")}");
    }
}
