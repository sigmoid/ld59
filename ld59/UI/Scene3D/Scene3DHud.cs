using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

namespace ld59.UI.Scene3D;

/// <summary>
/// The 2D layer a 3D view draws over its rendered image: crosshair, interact prompt, and the
/// developer overlays (<c>idview</c> / <c>depthview</c> picture-in-picture, editor pick
/// diagnostics). Pure presentation -- every value it shows is passed in.
/// </summary>
public sealed class Scene3DHud : IDisposable
{
    private const int PanelMargin = 12;

    private readonly Texture2D _pixel;

    public Scene3DHud()
    {
        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    /// <summary>
    /// The ID buffer picture-in-picture: grey = plain mesh, green = interactable, yellow = the
    /// entity currently hovered. Shows whether an object renders into the pick buffer at all and
    /// whether it's recognised as interactable.
    /// </summary>
    public void DrawIdDebug(SpriteBatch sb, Rectangle bounds, Texture2D idTarget,
                            int crosshairId, string hoverName)
    {
        int pw  = bounds.Width / 3;
        int ph  = pw * idTarget.Height / idTarget.Width;
        var dst = new Rectangle(bounds.Right - pw - PanelMargin, bounds.Y + PanelMargin, pw, ph);
        DrawFramed(sb, dst, idTarget);

        // Marker at the buffer centre (= the crosshair sample point).
        sb.Draw(_pixel, new Rectangle(dst.Center.X - 3, dst.Center.Y, 7, 1), Color.Magenta);
        sb.Draw(_pixel, new Rectangle(dst.Center.X, dst.Center.Y - 3, 1, 7), Color.Magenta);

        DrawCaption(sb, dst, $"id@crosshair={crosshairId}  hover={hoverName ?? "none"}");
    }

    /// <summary>
    /// The linear depth buffer picture-in-picture, plus the world distance under the crosshair.
    /// Near geometry is dark, the far plane is bright -- a flat black or flat white panel means the
    /// depth pass isn't seeing the scene. Distance is in red and the geometry mask in green, so it
    /// reads as a red ramp that turns yellow-ish past the encoding range and stays black where
    /// nothing was drawn.
    /// </summary>
    public void DrawDepthDebug(SpriteBatch sb, Rectangle bounds, Texture2D depthMap,
                               in RtViewport vp, Vector2 crosshairDepth, float farDistance)
    {
        int pw  = bounds.Width / 3;
        int ph  = pw * vp.Height / vp.Width;
        var dst = new Rectangle(bounds.Right - pw - PanelMargin, bounds.Bottom - ph - 40, pw, ph);
        DrawFramed(sb, dst, depthMap);

        DrawCaption(sb, dst, crosshairDepth.Y < 0.5f
            ? "depth@crosshair: background (no geometry)"
            : $"depth@crosshair: {crosshairDepth.X * farDistance:0.##}u  " +
              $"({crosshairDepth.X:0.###} of the {farDistance:0} range)");
    }

    public void DrawCrosshair(SpriteBatch sb, Rectangle bounds)
    {
        int cx = bounds.X + bounds.Width  / 2;
        int cy = bounds.Y + bounds.Height / 2;
        sb.Draw(_pixel, new Rectangle(cx - 8, cy - 1, 16, 2), Color.White * 0.8f);
        sb.Draw(_pixel, new Rectangle(cx - 1, cy - 8, 2, 16), Color.White * 0.8f);
    }

    /// <summary>"[E] Open the door" under the crosshair, for whatever the player is looking at.</summary>
    public void DrawInteractPrompt(SpriteBatch sb, Rectangle bounds, string promptText)
    {
        var font = Core.DefaultFont;
        string text = $"[E] {promptText}";
        var size = font.MeasureString(text);
        int tx = bounds.X + bounds.Width / 2 - (int)(size.X / 2);
        int ty = bounds.Y + bounds.Height / 2 + 24;
        sb.Draw(_pixel, new Rectangle(tx - 6, ty - 3, (int)size.X + 12, (int)size.Y + 6), Color.Black * 0.6f);
        sb.DrawString(font, text, new Vector2(tx, ty), Color.White);
    }

    /// <summary>Editor pick diagnostics, top-left of the viewport: live selection/gizmo state plus
    /// the last click's result, so picking can be debugged without a visible stdout console.</summary>
    public void DrawEditorDiag(SpriteBatch sb, Rectangle bounds, string line1, string line2)
    {
        var font = Core.DefaultFont;
        var p1 = new Vector2(bounds.X + 8, bounds.Y + 8);
        var p2 = new Vector2(bounds.X + 8, bounds.Y + 8 + font.LineSpacing);
        sb.Draw(_pixel, new Rectangle((int)p1.X - 4, (int)p1.Y - 2,
            (int)MathF.Max(font.MeasureString(line1).X, font.MeasureString(line2).X) + 8,
            font.LineSpacing * 2 + 4), Color.Black * 0.6f);
        sb.DrawString(font, line1, p1, Color.Yellow);
        sb.DrawString(font, line2, p2, Color.Yellow);
    }

    /// <summary>F4 live pick readout: what the CPU raycast reports under the cursor right now
    /// (== what a click would grab).</summary>
    public void DrawGizmoPickDiag(SpriteBatch sb, Rectangle bounds, string text)
    {
        var font = Core.DefaultFont;
        var tp = new Vector2(bounds.X + 8, bounds.Bottom - font.LineSpacing - 8);
        sb.Draw(_pixel, new Rectangle((int)tp.X - 4, (int)tp.Y - 2,
            (int)font.MeasureString(text).X + 8, font.LineSpacing + 4), Color.Black * 0.6f);
        sb.DrawString(font, text, tp, Color.Cyan);
    }

    // A debug buffer drawn with a 2px white border so it reads as a panel over the scene.
    private void DrawFramed(SpriteBatch sb, Rectangle dst, Texture2D texture)
    {
        sb.Draw(_pixel, new Rectangle(dst.X - 2, dst.Y - 2, dst.Width + 4, dst.Height + 4), Color.White);
        sb.Draw(texture, dst, Color.White);
    }

    // Boxed readout hung under a debug panel.
    private void DrawCaption(SpriteBatch sb, Rectangle panel, string text)
    {
        var font = Core.DefaultFont;
        var size = font.MeasureString(text);
        sb.Draw(_pixel, new Rectangle(panel.X - 2, panel.Bottom + 4, (int)size.X + 6, (int)size.Y + 4),
            Color.Black * 0.6f);
        sb.DrawString(font, text, new Vector2(panel.X + 1, panel.Bottom + 6), Color.Lime);
    }

    public void Dispose() => _pixel?.Dispose();
}
