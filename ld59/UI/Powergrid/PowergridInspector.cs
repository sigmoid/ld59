using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

namespace ld59.UI.Powergrid;

/// <summary>
/// Right-hand editor inspector. Top section edits the selected node (kind/anchor/goal/puzzle/tokens);
/// the bottom section edits level-wide config (inventory). Owns its buttons as children and lays
/// them out in SetBounds, so it follows the window when dragged.
/// </summary>
public sealed class PowergridInspector : UIPanel
{
    private Rectangle _bounds;
    private readonly PowergridView _view;
    private readonly SpriteFont _font;
    private readonly Texture2D _pixel;

    private readonly Button _kind, _anchor, _goal, _discovery, _tokMinus, _tokPlus, _heldMinus, _heldPlus, _delete;
    private readonly Button _capMinus, _capPlus;
    private readonly Button _inv1, _inv2, _invClr;

    private Vector2 _headerPos, _tokLabelPos, _heldLabelPos, _capLabelPos, _levelHeaderPos, _invLabelPos;

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
        _discovery= Mk("Discover",    _view.ToggleDiscovery);
        _tokMinus = Mk("-",           () => _view.AdjustPlacedToken(-1));
        _tokPlus  = Mk("+",           () => _view.AdjustPlacedToken(1));
        _heldMinus= Mk("-",           () => _view.AdjustHeldToken(-1));
        _heldPlus = Mk("+",           () => _view.AdjustHeldToken(1));
        _capMinus = Mk("-",           () => _view.AdjustTickCap(-8));
        _capPlus  = Mk("+",           () => _view.AdjustTickCap(8));
        _delete   = Mk("Delete Node", _view.DeleteSelected);

        _inv1   = Mk("+1",  () => _view.AddInventoryToken(1));
        _inv2   = Mk("+2",  () => _view.AddInventoryToken(2));
        _invClr = Mk("Clr", _view.ClearInventory);

        foreach (var b in new[] { _kind, _anchor, _goal, _discovery, _tokMinus, _tokPlus, _heldMinus, _heldPlus, _capMinus, _capPlus, _delete, _inv1, _inv2, _invClr })
            AddChild(b);

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

        _headerPos = new Vector2(x, y); y += 26;
        _kind.SetBounds(new Rectangle(x, y, w, h));     y += h + gap;
        _anchor.SetBounds(new Rectangle(x, y, w, h));    y += h + gap;
        _goal.SetBounds(new Rectangle(x, y, w, h));      y += h + gap;
        _discovery.SetBounds(new Rectangle(x, y, w, h)); y += h + gap;

        _tokLabelPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
        _tokPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
        _tokMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
        y += h + gap;

        _heldLabelPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
        _heldPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
        _heldMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
        y += h + gap;

        _capLabelPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
        _capPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
        _capMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
        y += h + gap;

        _delete.SetBounds(new Rectangle(x, y, w, h)); y += h + gap + 18;

        // Level section
        _levelHeaderPos = new Vector2(x, y); y += 26;
        _invLabelPos = new Vector2(x, y); y += 28;
        int third = (w - 2 * gap) / 3;
        _inv1.SetBounds(new Rectangle(x, y, third, h));
        _inv2.SetBounds(new Rectangle(x + third + gap, y, third, h));
        _invClr.SetBounds(new Rectangle(x + (third + gap) * 2, y, w - (third + gap) * 2, h));
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        var s = _view.Selected;
        bool en = s != null;
        foreach (var b in new[] { _kind, _anchor, _goal, _discovery, _tokMinus, _tokPlus, _heldMinus, _heldPlus, _capMinus, _capPlus, _delete })
            b.SetEnabled(en);

        _kind.SetText(s != null ? $"Kind: {s.NodeKind}" : "Kind: -");
        _anchor.SetText(s != null ? $"Anchor: {(s.IsAnchor ? "ON" : "off")}" : "Anchor: -");
        _goal.SetText(s != null ? $"Goal: {(s.IsGoal ? "ON" : "off")}" : "Goal: -");
        _discovery.SetText(s != null ? $"Discover: {(_view.SelectedPuzzleDiscovery ? "ON" : "off")}" : "Discover: -");
    }

    public override void Draw(SpriteBatch sb)
    {
        sb.Draw(_pixel, _bounds, null, ColorPalette.White, 0f, Vector2.Zero, SpriteEffects.None, 0.5f);
        sb.Draw(_pixel, new Rectangle(_bounds.X, _bounds.Y, 2, _bounds.Height), null, ColorPalette.Black,
            0f, Vector2.Zero, SpriteEffects.None, 0.51f);

        var s = _view.Selected;
        sb.DrawString(_font, "Inspector", _headerPos, ColorPalette.Black);
        sb.DrawString(_font, $"Token: {(s?.PlacedTokenPower ?? 0)}", _tokLabelPos, ColorPalette.Black);
        sb.DrawString(_font, $"Held:  {(s?.HeldTokenPower ?? 0)}", _heldLabelPos, ColorPalette.Black);
        sb.DrawString(_font, $"Tick cap: {(s != null ? _view.SelectedPuzzleTickCap : 0)}", _capLabelPos, ColorPalette.Black);

        sb.DrawString(_font, "Level", _levelHeaderPos, ColorPalette.Black);
        sb.DrawString(_font, $"Inv: {_view.EditorInventoryText}", _invLabelPos, ColorPalette.Black);

        base.Draw(sb);
    }

    public override void OnRemovedFromUI() => _pixel?.Dispose();
}
