using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

namespace ld59.UI.Powergrid;

/// <summary>
/// Right-hand inspector panel for the selected node. Owns its action buttons as children and
/// repositions them in SetBounds, so it follows the window when dragged. Reads the current
/// selection from the view each frame to refresh labels and enabled state.
/// </summary>
public sealed class PowergridInspector : UIPanel
{
    private Rectangle _bounds;
    private readonly PowergridView _view;
    private readonly SpriteFont _font;
    private readonly Texture2D _pixel;

    private readonly Button _kind, _anchor, _goal, _ancMinus, _ancPlus, _tokMinus, _tokPlus, _delete;

    private Vector2 _headerPos, _ancLabelPos, _tokLabelPos;

    public PowergridInspector(Rectangle bounds, PowergridView view)
    {
        _view = view;
        _font = Core.DefaultFont;
        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        Button Mk(string text, Action onClick) => new(
            new Rectangle(0, 0, 10, 10), text, _font,
            ColorPalette.Black, ColorPalette.DarkCream, ColorPalette.ActualWhite, onClick);

        _kind     = Mk("Kind",        _view.CycleKind);
        _anchor   = Mk("Anchor",      _view.ToggleAnchor);
        _goal     = Mk("Goal",        _view.ToggleGoal);
        _ancMinus = Mk("-",           () => _view.AdjustPlacedToken(-1));
        _ancPlus  = Mk("+",           () => _view.AdjustPlacedToken(1));
        _tokMinus = Mk("-",           () => _view.AdjustHeldToken(-1));
        _tokPlus  = Mk("+",           () => _view.AdjustHeldToken(1));
        _delete   = Mk("Delete Node", _view.DeleteSelected);

        AddChild(_kind); AddChild(_anchor); AddChild(_goal);
        AddChild(_ancMinus); AddChild(_ancPlus); AddChild(_tokMinus); AddChild(_tokPlus);
        AddChild(_delete);

        SetBounds(bounds);
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        const int pad = 8, h = 30, gap = 6;
        int x = _bounds.X + pad;
        int w = _bounds.Width - 2 * pad;
        int y = _bounds.Y + pad;

        _headerPos = new Vector2(x, y);
        y += 24;

        _kind.SetBounds(new Rectangle(x, y, w, h));   y += h + gap;
        _anchor.SetBounds(new Rectangle(x, y, w, h));  y += h + gap;
        _goal.SetBounds(new Rectangle(x, y, w, h));    y += h + gap;

        _ancLabelPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
        _ancPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
        _ancMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
        y += h + gap;

        _tokLabelPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
        _tokPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
        _tokMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
        y += h + gap + 10;

        _delete.SetBounds(new Rectangle(x, y, w, h));
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        var s = _view.Selected;
        bool en = s != null;
        foreach (var b in new[] { _kind, _anchor, _goal, _ancMinus, _ancPlus, _tokMinus, _tokPlus, _delete })
            b.SetEnabled(en);

        _kind.SetText(s != null ? $"Kind: {s.NodeKind}" : "Kind: -");
        _anchor.SetText(s != null ? $"Anchor: {(s.IsAnchor ? "ON" : "off")}" : "Anchor: -");
        _goal.SetText(s != null ? $"Goal: {(s.IsGoal ? "ON" : "off")}" : "Goal: -");
    }

    public override void Draw(SpriteBatch sb)
    {
        sb.Draw(_pixel, _bounds, null, ColorPalette.White, 0f, Vector2.Zero, SpriteEffects.None, 0.5f);
        sb.Draw(_pixel, new Rectangle(_bounds.X, _bounds.Y, 2, _bounds.Height), null, ColorPalette.Black,
            0f, Vector2.Zero, SpriteEffects.None, 0.51f);

        var s = _view.Selected;
        sb.DrawString(_font, "Inspector", _headerPos, ColorPalette.Black);
        sb.DrawString(_font, $"Token: {(s?.PlacedTokenPower ?? 0)}", _ancLabelPos, ColorPalette.Black);
        sb.DrawString(_font, $"Held:  {(s?.HeldTokenPower ?? 0)}", _tokLabelPos, ColorPalette.Black);

        base.Draw(sb);
    }

    public override void OnRemovedFromUI() => _pixel?.Dispose();
}
