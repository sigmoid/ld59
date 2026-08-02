using System.Globalization;
using Microsoft.Xna.Framework;
using Quartz;
using ld59.UI;

/// <summary>
/// Console command <c>ssao</c>: tunes the screen-space ambient occlusion over every open 3D view.
/// <code>
/// ssao                     show the current settings
/// ssao on | off            enable/disable
/// ssao radius 1.2          hemisphere radius in world units -- the first knob to reach for
/// ssao intensity 0.8       how strongly occlusion darkens (0 = off, skips the pass)
/// ssao power 2             contrast on the occlusion term; >1 confines it to real creases
/// ssao bias 0.05           world-unit slack against flat surfaces shading themselves
/// ssao samples 24          hemisphere taps per pixel (4-32)
/// ssao blur 2              bilateral blur taps each way (0 = raw, noisy AO)
/// ssao downscale 2         render the AO map at 1/N resolution (1-4)
/// ssao fade 80             world distance where AO fades out (0 = never)
/// ssao levels 4            quantise into N bands (0 = smooth, the default); see the note below
/// ssao dither 1 [scale]    ordered-dither the band edges (0 = hard steps), cell size in pixels
/// ssao color 20,20,30      colour occluded pixels darken toward, 0-255 per channel
/// ssao match               toggle matching the 1-bit palette's dark colour while 1-bit is on
/// ssao debug ao|normals|off    show the AO map or the reconstructed normals instead of the scene
/// </code>
/// <para>
/// If it shimmers as you walk: that is sampling noise, not a wrong setting. The pattern is anchored
/// to the screen and the geometry slides under it, so raise <c>samples</c> and <c>blur</c> (in that
/// order) until it settles. <c>levels</c> makes it worse, not better, until the AO underneath is
/// already clean -- banding a noisy signal just makes whole regions flip band together.
/// </para>
/// <para>
/// <c>debug normals</c> before <c>debug ao</c> when something looks structurally wrong: everything
/// here is downstream of the reconstructed normals, and a bad normal field is obvious there and
/// nearly unreadable from the AO map.
/// </para>
/// </summary>
public class SSAOCommandHandler : ConsoleCommandHandler
{
    public SSAOCommandHandler()
    {
        CommandName = "ssao";
    }

    public override void Execute(string[] args)
    {
        var views = UI3DScene.Instances;
        if (views.Count == 0)
        {
            Console.PrintLine("ssao: no 3D view is open.");
            return;
        }

        string sub = args != null && args.Length >= 1 ? args[0].ToLowerInvariant() : "";

        switch (sub)
        {
            case "":
                break;   // fall through to the readout below

            case "on":
            case "off":
                foreach (var v in views) v.Ssao.Enabled = sub == "on";
                break;

            case "radius" when TryFloat(args, 1, out float radius):
                foreach (var v in views) v.Ssao.Radius = MathHelper.Max(radius, 0.001f);
                break;

            case "intensity" when TryFloat(args, 1, out float intensity):
                foreach (var v in views) v.Ssao.Intensity = MathHelper.Clamp(intensity, 0f, 2f);
                // Setting a strength is a clear signal you want to see it -- the pass is off by
                // default, and silently doing nothing here reads as a broken command.
                if (intensity > 0f) foreach (var v in views) v.Ssao.Enabled = true;
                break;

            case "power" when TryFloat(args, 1, out float power):
                foreach (var v in views) v.Ssao.Power = MathHelper.Clamp(power, 0.1f, 8f);
                break;

            case "bias" when TryFloat(args, 1, out float bias):
                foreach (var v in views) v.Ssao.Bias = MathHelper.Max(bias, 0f);
                break;

            case "samples" when TryFloat(args, 1, out float samples):
                foreach (var v in views) v.Ssao.Samples = MathHelper.Clamp(samples, 4f, 32f);
                break;

            case "blur" when TryFloat(args, 1, out float blur):
                foreach (var v in views) v.Ssao.BlurRadius = MathHelper.Clamp(blur, 0f, 8f);
                break;

            case "downscale" when TryFloat(args, 1, out float downscale):
                foreach (var v in views) v.Ssao.Downscale = (int)MathHelper.Clamp(downscale, 1f, 4f);
                break;

            case "fade" when TryFloat(args, 1, out float fade):
                foreach (var v in views) v.Ssao.FadeDistance = MathHelper.Max(fade, 0f);
                break;

            case "levels" when TryFloat(args, 1, out float levels):
                foreach (var v in views) v.Ssao.Levels = MathHelper.Clamp(levels, 0f, 32f);
                break;

            case "dither" when TryFloat(args, 1, out float dither):
                foreach (var v in views)
                {
                    v.Ssao.Dither = MathHelper.Clamp(dither, 0f, 1f);
                    if (TryFloat(args, 2, out float scale))
                        v.Ssao.DitherScale = MathHelper.Clamp(scale, 1f, 16f);
                }
                break;

            case "color":
            case "colour":
                if (args.Length < 2 || !TryColor(args[1], out var color))
                {
                    Console.PrintLine("ssao: expected r,g,b (0-255), e.g. `ssao color 20,20,30`");
                    return;
                }
                foreach (var v in views) v.Ssao.OcclusionColor = color;
                break;

            case "match":
                foreach (var v in views) v.Ssao.MatchOneBitPalette = !v.Ssao.MatchOneBitPalette;
                break;

            case "debug":
            {
                string mode = args.Length >= 2 ? args[1].ToLowerInvariant() : "ao";
                var view = mode switch
                {
                    "ao" or "occlusion"    => SSAOPostProcessEffect.DebugMode.Ao,
                    "normals" or "normal"  => SSAOPostProcessEffect.DebugMode.Normals,
                    "off" or "none"        => SSAOPostProcessEffect.DebugMode.Off,
                    _ => SSAOPostProcessEffect.DebugMode.Off,
                };
                foreach (var v in views)
                {
                    v.Ssao.Debug = view;
                    // The debug views are only readable as colour, and they'd otherwise never be
                    // drawn while disabled (the chain skips the effect entirely).
                    if (view != SSAOPostProcessEffect.DebugMode.Off) v.Ssao.Enabled = true;
                }
                Console.PrintLine($"ssao debug: {view}"
                    + (view == SSAOPostProcessEffect.DebugMode.Off
                        ? "" : "  (run `1bit` to drop the dithering, or these are unreadable)"));
                return;
            }

            default:
                Console.PrintLine("usage: ssao [on|off|radius <u>|intensity <0-2>|power <n>|bias <u>|" +
                                  "samples <4-32>|blur <0-8>|downscale <1-4>|fade <dist>|levels <n>|" +
                                  "dither <0-1> [scale]|color <r,g,b>|match|debug ao|normals|off]");
                return;
        }

        var s = views[0].Ssao;
        Console.PrintLine($"ssao {(s.Enabled ? "on" : "off")}  radius={s.Radius:0.###}u  " +
                          $"intensity={s.Intensity:0.##}  power={s.Power:0.##}  bias={s.Bias:0.###}u");
        Console.PrintLine($"    samples={s.Samples:0}  blur={s.BlurRadius:0}  " +
                          $"downscale=1/{s.Downscale}  " +
                          $"fade={(s.FadeDistance <= 0f ? "off" : $"{s.FadeDistance:0.#}u")}");
        Console.PrintLine($"    levels={(s.Levels < 1.5f ? "smooth" : $"{s.Levels:0}")}  " +
                          $"dither={s.Dither:0.##} (cell {s.DitherScale:0}px)  " +
                          $"color={s.OcclusionColor.R},{s.OcclusionColor.G},{s.OcclusionColor.B}" +
                          (s.MatchOneBitPalette ? " (matching 1-bit palette)" : ""));
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
