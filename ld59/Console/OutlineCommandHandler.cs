using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;
using ld59.UI;

/// <summary>
/// Console command <c>outline</c>: tunes the silhouette outlines over every open 3D view.
/// <code>
/// outline                  show the current settings
/// outline on | off         enable/disable
/// outline width 2          line width in pixels (any integer, even widths included)
/// outline color 20,20,30   line colour, 0-255 per channel
/// outline opacity 0.6      how strongly the line blends over the scene
/// outline fade 120         world distance where outlines fade out (0 = never)
/// outline match            toggle matching the 1-bit palette's dark colour while 1-bit is on
/// outline debug ids|mask|off   show the id buffer or the bare edge mask instead of the scene
/// </code>
/// </summary>
public class OutlineCommandHandler : ConsoleCommandHandler
{
    public OutlineCommandHandler()
    {
        CommandName = "outline";
    }

    public override void Execute(string[] args)
    {
        var views = UI3DScene.Instances;
        if (views.Count == 0)
        {
            Console.PrintLine("outline: no 3D view is open.");
            return;
        }

        string sub = args != null && args.Length >= 1 ? args[0].ToLowerInvariant() : "";

        switch (sub)
        {
            case "":
                break;   // fall through to the readout below

            case "on":
            case "off":
                foreach (var v in views) v.Outline.Enabled = sub == "on";
                break;

            case "width" when TryFloat(args, 1, out float width):
                foreach (var v in views)
                    v.Outline.Width = (int)MathHelper.Clamp(
                        width, 1, OutlinePostProcessEffect.MaxWidth);
                break;

            // Deliberately not an alias for `width`: it used to mean a dilation RADIUS, so
            // `thickness 2` drew 5px. Silently reinterpreting the same number as 2px would be a
            // trap, so say so instead.
            case "thickness":
                Console.PrintLine("outline: `thickness` (a radius) is now `width` (pixels) -- " +
                                  "old thickness t is width 1+2t, so `thickness 1` is `width 3`.");
                return;

            case "color":
            case "colour":
                if (args.Length < 2 || !TryColor(args[1], out var color))
                {
                    Console.PrintLine("outline: expected r,g,b (0-255), e.g. `outline color 20,20,30`");
                    return;
                }
                foreach (var v in views) v.Outline.OutlineColor = color;
                break;

            case "opacity" when TryFloat(args, 1, out float opacity):
                foreach (var v in views) v.Outline.Opacity = MathHelper.Clamp(opacity, 0f, 1f);
                break;

            case "fade" when TryFloat(args, 1, out float fade):
                foreach (var v in views) v.Outline.FadeDistance = fade;
                break;

            case "debug":
            {
                string mode = args.Length >= 2 ? args[1].ToLowerInvariant() : "ids";
                var view = mode switch
                {
                    "ids" or "id"   => OutlinePostProcessEffect.DebugMode.Ids,
                    "mask" or "edge" => OutlinePostProcessEffect.DebugMode.Mask,
                    "off" or "none" => OutlinePostProcessEffect.DebugMode.Off,
                    _ => OutlinePostProcessEffect.DebugMode.Off,
                };
                foreach (var v in views)
                {
                    v.Outline.Debug = view;
                    // The debug views are only readable as colour, and they'd otherwise be drawn
                    // while disabled (the chain skips the effect entirely).
                    if (view != OutlinePostProcessEffect.DebugMode.Off) v.Outline.Enabled = true;
                }
                Console.PrintLine($"outline debug: {view}"
                    + (view == OutlinePostProcessEffect.DebugMode.Off
                        ? "" : "  (run `1bit` to drop the dithering, or these are unreadable)"));
                return;
            }

            case "stats":
                PrintIdStats(views[0]);
                return;

            case "match":
                foreach (var v in views) v.Outline.MatchOneBitPalette = !v.Outline.MatchOneBitPalette;
                break;

            default:
                Console.PrintLine("usage: outline [on|off|width <px>|color <r,g,b>|opacity <0-1>|" +
                                  "fade <dist>|match|debug ids|mask|off]");
                return;
        }

        var o = views[0].Outline;
        Console.PrintLine($"outline {(o.Enabled ? "on" : "off")}  width={o.Width}px  " +
                          $"opacity={o.Opacity:0.##}  " +
                          $"fade={(o.FadeDistance <= 0f ? "off" : $"{o.FadeDistance:0.#}u")}");
        Console.PrintLine($"    color={o.OutlineColor.R},{o.OutlineColor.G},{o.OutlineColor.B}" +
                          (o.MatchOneBitPalette ? " (matching 1-bit palette)" : ""));
    }

    // Reads the id buffer back on the CPU and reports what's actually in it. Colours in the `debug
    // ids` view are easy to misread (and go through the rest of the pipeline before you see them);
    // this is the same data as plain numbers -- how many surfaces the outline pass can actually
    // tell apart, and how much of the frame it thinks is empty.
    private void PrintIdStats(UI3DScene view)
    {
        var map = view.Scene.DepthMap;
        if (map == null || map.IsDisposed)
        {
            Console.PrintLine("outline stats: no depth/id buffer yet (is anything using it enabled?)");
            return;
        }

        var pixels = new Vector2[map.Width * map.Height];
        map.GetData(pixels);

        var counts = new System.Collections.Generic.Dictionary<int, int>();
        int background = 0;
        float maxDepth = 0f;
        foreach (var p in pixels)
        {
            if (p.Y < 0.5f) { background++; continue; }
            int id = (int)System.MathF.Round(p.Y);
            counts.TryGetValue(id, out int n);
            counts[id] = n + 1;
            if (p.X > maxDepth) maxDepth = p.X;
        }

        int total = pixels.Length;
        Console.PrintLine($"outline stats: {map.Width}x{map.Height}, {counts.Count} distinct ids, " +
                          $"background {100f * background / total:0.#}%, deepest {maxDepth:0.###} of range");

        var ordered = counts.OrderByDescending(kv => kv.Value).Take(16);
        foreach (var kv in ordered)
            Console.PrintLine($"    id {kv.Key,-4} {100f * kv.Value / total,5:0.#}% of frame");
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
