using Quartz;

/// <summary>
/// Console command <c>posterize</c>: sets how many bands the 3D lighting is quantized into
/// (0 or 1 = off). Affects only lit 3D surfaces — not UI, 2D, or the 1-bit post-process.
/// Usage: <c>posterize 4</c> to band, <c>posterize 0</c> to disable.
/// </summary>
public class PosterizeCommandHandler : ConsoleCommandHandler
{
    public PosterizeCommandHandler()
    {
        CommandName = "posterize";
    }

    public override void Execute(string[] args)
    {
        if (args != null && args.Length >= 1 && float.TryParse(args[0], out float levels))
            SceneLightData.PosterizeLevels = levels;

        Console.PrintLine($"3D lighting posterize: {(SceneLightData.PosterizeLevels < 1.5f ? "off" : $"{SceneLightData.PosterizeLevels:0} bands")}");
    }
}
