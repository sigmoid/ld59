using Quartz;
using Quartz.Graphics;

/// <summary>
/// Console command <c>1bit</c>: toggles the one-bit dithering post-process effect on/off.
/// </summary>
public class OneBitCommandHandler : ConsoleCommandHandler
{
    public OneBitCommandHandler()
    {
        CommandName = "1bit";
    }

    public override void Execute(string[] args)
    {
        var effect = Core.PostProcessing.GetEffect<OneBitDitheringPostProcessEffect>();
        if (effect == null)
        {
            Console.PrintLine("1-bit effect is not registered.");
            return;
        }

        effect.Enabled = !effect.Enabled;
        Console.PrintLine($"1-bit effect: {(effect.Enabled ? "on" : "off")}");
    }
}
