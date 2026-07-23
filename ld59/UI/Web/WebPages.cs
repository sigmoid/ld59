using System;
using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

/// <summary>
/// Registration point for code-built browser pages. Each factory receives the browser's page bounds
/// and a navigate callback, and returns the subtree to display — the same way any other panel in the
/// project builds children into a content rect.
/// </summary>
public static class WebPages
{
    public static void RegisterAll()
    {
        WebPageRegistry.Register("tools/index.txt", BuildToolsPage);
    }

    private static UIElement BuildToolsPage(Rectangle bounds, Action<string> navigate)
    {
        var root = new Canvas(bounds, ColorPalette.ActualWhite);
        var font = Core.DefaultFont;

        const int pad = 16;
        int x = bounds.X + pad;
        int w = bounds.Width - pad * 2;
        int y = bounds.Y + pad;

        root.AddChild(new Label(new Rectangle(x, y, w, 30),
            "LithNET Diagnostics", font, ColorPalette.DarkGreen));
        y += 36;

        root.AddChild(new Label(new Rectangle(x, y, w, 24),
            "This page is built in C#, not authored as text.", font, ColorPalette.Black));
        y += 40;

        var readout = new Label(new Rectangle(x + 210, y, 120, 24),
            "Signal: 50%", font, ColorPalette.DarkGreen);

        var slider = new Slider(new Rectangle(x, y, 200, 24),
            minValue: 0f, maxValue: 100f, initialValue: 50f, step: 1f, isHorizontal: true,
            trackColor: ColorPalette.LightGreen, fillColor: ColorPalette.Green,
            handleColor: ColorPalette.DarkGreen, handleHoverColor: ColorPalette.Green,
            handlePressedColor: ColorPalette.DarkGreen,
            trackHeight: 6, handleSize: 18, handleBorderSize: 1);
        slider.OnValueChanged += v => readout.Text = $"Signal: {(int)v}%";

        root.AddChild(slider);
        root.AddChild(readout);
        y += 44;

        root.AddChild(WebLink.Create(new Rectangle(x, y, 220, 24),
            "< Back to LithNET home", "home.txt", navigate, font));

        return root;
    }
}
