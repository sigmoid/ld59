using System.Globalization;
using Microsoft.Xna.Framework;
using Quartz;
using ld59.UI;

/// <summary>
/// Console command <c>fog</c>: tunes the exponential fog over every open 3D view.
/// <code>
/// fog                     show the current settings
/// fog on | off            enable/disable
/// fog density 0.03        fog per world unit at the base height (0 = off)
/// fog color 90,100,120    fog colour, 0-255 per channel
/// fog match               toggle matching the 1-bit palette's bright colour while 1-bit is on
/// fog start 8             world units of clear air in front of the camera
/// fog height 0.05 [base]  height falloff (0 = uniform fog) and the height it's measured from
/// fog noise 0.6 [scale] [dist]   fbm strength, world frequency, fade-in distance
/// fog speed 1.5           how fast the noise churns (multiplied by delta time)
/// fog levels 5            quantise the fog into N bands (0 = smooth); reads better in 1-bit
/// fog dither 1 [scale]    ordered-dither the band edges (0 = hard steps), cell size in pixels
/// fog max 0.8             ceiling on the blend (1 = distance resolves to solid fog colour)
/// fog sky 0.3             how much fog the background gets (1 = fully fogged horizon)
/// fog depth               toggle the full-screen depth debug output of the fog pass
/// </code>
/// </summary>
public class FogCommandHandler : ConsoleCommandHandler
{
    public FogCommandHandler()
    {
        CommandName = "fog";
    }

    public override void Execute(string[] args)
    {
        var views = UI3DScene.Instances;
        if (views.Count == 0)
        {
            Console.PrintLine("fog: no 3D view is open.");
            return;
        }

        string sub = args != null && args.Length >= 1 ? args[0].ToLowerInvariant() : "";

        switch (sub)
        {
            case "":
                break;   // fall through to the readout below

            case "on":
            case "off":
                foreach (var v in views) v.Fog.Enabled = sub == "on";
                break;

            case "density" when TryFloat(args, 1, out float density):
                foreach (var v in views) v.Fog.Density = density;
                // Density is what gates the depth pass, so turning it up implies you want fog on.
                if (density > 0f) foreach (var v in views) v.Fog.Enabled = true;
                break;

            case "color":
            case "colour":
                if (args.Length < 2 || !TryColor(args[1], out var color))
                {
                    Console.PrintLine("fog: expected r,g,b (0-255), e.g. `fog color 90,100,120`");
                    return;
                }
                foreach (var v in views) v.Fog.FogColor = color;
                break;

            case "start" when TryFloat(args, 1, out float start):
                foreach (var v in views) v.Fog.Start = start;
                break;

            case "height" when TryFloat(args, 1, out float falloff):
                foreach (var v in views) v.Fog.HeightFalloff = falloff;
                if (TryFloat(args, 2, out float baseHeight))
                    foreach (var v in views) v.Fog.BaseHeight = baseHeight;
                break;

            case "speed" when TryFloat(args, 1, out float speed):
                foreach (var v in views) v.Fog.NoiseSpeed = speed;
                break;

            case "noise" when TryFloat(args, 1, out float strength):
                foreach (var v in views) v.Fog.NoiseStrength = strength;
                if (TryFloat(args, 2, out float scale))
                    foreach (var v in views) v.Fog.NoiseScale = scale;
                if (TryFloat(args, 3, out float noiseDist))
                    foreach (var v in views) v.Fog.NoiseDistance = noiseDist;
                break;

            case "match":
                foreach (var v in views) v.Fog.MatchOneBitPalette = !v.Fog.MatchOneBitPalette;
                break;

            case "levels" when TryFloat(args, 1, out float levels):
                foreach (var v in views) v.Fog.Levels = levels;
                break;

            case "dither" when TryFloat(args, 1, out float dither):
                foreach (var v in views) v.Fog.Dither = dither;
                if (TryFloat(args, 2, out float ditherScale))
                    foreach (var v in views) v.Fog.DitherScale = ditherScale;
                break;

            case "max" when TryFloat(args, 1, out float maxFog):
                foreach (var v in views) v.Fog.MaxFog = MathHelper.Clamp(maxFog, 0f, 1f);
                break;

            case "sky" when TryFloat(args, 1, out float sky):
                foreach (var v in views) v.Fog.BackgroundFog = MathHelper.Clamp(sky, 0f, 1f);
                break;

            case "depth":
                foreach (var v in views) v.Fog.ShowDepth = !v.Fog.ShowDepth;
                break;

            default:
                Console.PrintLine("usage: fog [on|off|density <f>|color <r,g,b>|start <f>|" +
                                  "height <falloff> [base]|noise <strength> [scale] [dist]|speed <f>|" +
                                  "levels <n>|dither <0-1> [scale]|match|max <0-1>|sky <0-1>|depth]");
                return;
        }

        var f = views[0].Fog;
        Console.PrintLine($"fog {(f.Enabled ? "on" : "off")}  density={f.Density:0.####}  start={f.Start:0.##}  " +
                          $"color={f.FogColor.R},{f.FogColor.G},{f.FogColor.B}" +
                          (f.MatchOneBitPalette ? " (matching 1-bit palette)" : "") + "  " +
                          $"levels={(f.Levels < 1.5f ? "smooth" : $"{f.Levels:0}")}  " +
                          $"dither={f.Dither:0.##}@{f.DitherScale:0.#}px");
        Console.PrintLine($"    height falloff={f.HeightFalloff:0.####} base={f.BaseHeight:0.##}  max={f.MaxFog:0.##}  sky={f.BackgroundFog:0.##}  " +
                          $"noise={f.NoiseStrength:0.##} scale={f.NoiseScale:0.####} dist={f.NoiseDistance:0.#} " +
                          $"speed={f.NoiseSpeed:0.##}" +
                          (f.ShowDepth ? "  [depth debug]" : ""));
    }

    private static bool TryFloat(string[] args, int index, out float value)
    {
        value = 0f;
        return args != null && args.Length > index &&
               float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryColor(string text, out Color color)
    {
        color = Color.White;
        var parts = text.Split(',');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int r) ||
            !int.TryParse(parts[1], out int g) ||
            !int.TryParse(parts[2], out int b)) return false;
        color = new Color(
            MathHelper.Clamp(r, 0, 255),
            MathHelper.Clamp(g, 0, 255),
            MathHelper.Clamp(b, 0, 255));
        return true;
    }
}
