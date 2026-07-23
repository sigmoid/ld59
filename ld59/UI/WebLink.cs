using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

/// <summary>
/// Builds link-styled buttons for code-built browser pages, so pages do not each re-invent
/// link colouring. Colour reflects whether the target has been visited.
/// </summary>
public static class WebLink
{
    public static Button Create(Rectangle bounds, string text, string url, Action<string> navigate,
        SpriteFont font = null)
    {
        var textColor = WebPage.VisitedUrls.Contains(url) ? ColorPalette.VisitedLink : ColorPalette.Black;

        return new Button(bounds, text, font ?? Core.DefaultFont,
            Color.Transparent, ColorPalette.LightGreen, textColor,
            () => navigate?.Invoke(url));
    }
}
