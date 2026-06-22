using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

namespace ld59.UI.Powergrid;

/// <summary>
/// Right-hand editor inspector. Top section edits the selected node (its fixed-rune clue and
/// deletion); the middle section authors the selected node's puzzle (sequence order and the runes it
/// rewards); the bottom section edits level-wide config — the player's starting rune inventory and
/// the active adjacency rules. Owns its buttons as children and lays them out in SetBounds, so it
/// follows the window when dragged.
///
/// The inventory/reward editor reuses the play-mode <b>alphabet pyramid</b>: every rune is a chip
/// laid out by tier/side, showing its current count. Left-click a chip to add one, right-click to
/// remove one. A target toggle picks whether the chips edit the level inventory or the selected
/// puzzle's reward.
/// </summary>
public sealed class PowergridInspector : UIPanel
{
    /// <summary>Rules surfaced as editor toggles, in display order. (DifferentTier is intentionally
    /// omitted — kept in code but not used.)</summary>
    private static readonly ColoringRule[] ExposedRules =
    {
        ColoringRule.DifferentRune, ColoringRule.TierStep, ColoringRule.Sidedness,
    };

    /// <summary>What the rune-chip pyramid is editing.</summary>
    private enum ChipTarget { Inventory, Reward }

    private const int ChipGap = 6;

    private Rectangle _bounds;
    private readonly PowergridView _view;
    private readonly SpriteFont _font;
    private readonly Texture2D _pixel;
    private readonly Texture2D _circleFilled;
    private readonly Texture2D _circleOutlined;

    private readonly Button _fixedRune, _delete;
    private readonly Button _puzOrderMinus, _puzOrderPlus;
    private readonly Button _targetToggle;
    private readonly Dictionary<ColoringRule, Button> _ruleButtons = new();

    private ChipTarget _target = ChipTarget.Inventory;

    // Pyramid layout area (recomputed in SetBounds).
    private Rectangle _gridArea;

    // Mouse edge-detection for chip clicks.
    private bool _prevLeft, _prevRight;

    private Vector2 _headerPos, _puzHeaderPos, _puzOrderPos;
    private Vector2 _levelHeaderPos, _gridHintPos, _rulesLabelPos;

    public PowergridInspector(Rectangle bounds, PowergridView view)
    {
        _view = view;
        _font = Core.DefaultFont;
        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _circleFilled   = Core.Content.Load<Texture2D>("images/powergrid/circle-filled");
        _circleOutlined = Core.Content.Load<Texture2D>("images/powergrid/circle-outlined");

        Button Mk(string text, Action onClick) => new(
            new Rectangle(0, 0, 10, 10), text, _font,
            ColorPalette.Black, ColorPalette.DarkCream, ColorPalette.ActualWhite, onClick);

        _fixedRune = Mk("Fill", _view.CycleFixedRune);
        _delete    = Mk("Delete Node", _view.DeleteSelected);
        _puzOrderMinus = Mk("-", () => _view.AdjustPuzzleOrder(-1));
        _puzOrderPlus  = Mk("+", () => _view.AdjustPuzzleOrder(1));
        _targetToggle  = Mk("Target: Inventory",
            () => _target = _target == ChipTarget.Inventory ? ChipTarget.Reward : ChipTarget.Inventory);

        AddChild(_fixedRune);
        AddChild(_delete);
        AddChild(_puzOrderMinus);
        AddChild(_puzOrderPlus);
        AddChild(_targetToggle);

        foreach (var rule in ExposedRules)
        {
            var captured = rule;
            var btn = Mk(ColoringRules.ShortName(rule), () => _view.ToggleRule(captured));
            _ruleButtons[rule] = btn;
            AddChild(btn);
        }

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
        _fixedRune.SetBounds(new Rectangle(x, y, w, h)); y += h + gap;
        _delete.SetBounds(new Rectangle(x, y, w, h));    y += h + gap + 18;

        // Per-puzzle sequence authoring (acts on the selected node's puzzle).
        _puzHeaderPos = new Vector2(x, y); y += 26;
        _puzOrderPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
        _puzOrderPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
        _puzOrderMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
        y += h + gap + 18;

        // Level config: target toggle + the rune-chip pyramid + rules.
        _levelHeaderPos = new Vector2(x, y); y += 26;
        _targetToggle.SetBounds(new Rectangle(x, y, w, h)); y += h + gap;
        _gridHintPos = new Vector2(x, y); y += 20;

        // The pyramid takes the space between the hint and the (bottom-anchored) rules block.
        int rulesHeight = 26 + ExposedRules.Length * (h + gap);
        int gridBottom = _bounds.Bottom - pad - rulesHeight;
        _gridArea = new Rectangle(x, y, w, Math.Max(80, gridBottom - y));

        y = _gridArea.Bottom + 6;
        _rulesLabelPos = new Vector2(x, y); y += 26;
        foreach (var rule in ExposedRules)
        {
            _ruleButtons[rule].SetBounds(new Rectangle(x, y, w, h));
            y += h + gap;
        }
    }

    /// <summary>Lays the whole alphabet out as a pyramid (tier = row, RowOrder = position, each row
    /// centred) within <see cref="_gridArea"/>, returning each rune's chip rect — mirroring the play
    /// view so the editor reads the same. Used for both drawing and click hit-testing.</summary>
    private List<(string name, Rectangle rect)> PyramidSlots()
    {
        var slots = new List<(string, Rectangle)>();
        var tiers = SymbolDictionary.All
            .GroupBy(s => s.Tier)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(s => s.RowOrder).ToList())
            .ToList();
        if (tiers.Count == 0) return slots;

        int cols = tiers.Max(t => t.Count);
        int chipW = (_gridArea.Width - (cols - 1) * ChipGap) / cols;
        int chipH = (_gridArea.Height - (tiers.Count - 1) * ChipGap) / tiers.Count;
        int chip = Math.Max(8, Math.Min(chipW, chipH));

        int totalH = tiers.Count * chip + (tiers.Count - 1) * ChipGap;
        int y = _gridArea.Y + (_gridArea.Height - totalH) / 2;
        foreach (var row in tiers)
        {
            int rowW = row.Count * chip + (row.Count - 1) * ChipGap;
            int x = _gridArea.X + (_gridArea.Width - rowW) / 2;
            foreach (var s in row)
            {
                slots.Add((s.Name, new Rectangle(x, y, chip, chip)));
                x += chip + ChipGap;
            }
            y += chip + ChipGap;
        }
        return slots;
    }

    /// <summary>Current count shown on a chip, per the active target.</summary>
    private int ChipCount(string rune)
        => _target == ChipTarget.Inventory
            ? _view.EditorInventoryCount(rune)
            : _view.SelectedPuzzleRewardCount(rune);

    private void ApplyChip(string rune, int delta)
    {
        if (_target == ChipTarget.Inventory) _view.AdjustInventoryRune(rune, delta);
        else _view.AdjustPuzzleReward(rune, delta);
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        var s = _view.Selected;
        bool en = s != null;
        _fixedRune.SetEnabled(en);
        _delete.SetEnabled(en);
        _puzOrderMinus.SetEnabled(en);
        _puzOrderPlus.SetEnabled(en);

        var fill = _view.SelectedFixedRune;
        _fixedRune.SetText(s == null ? "Fill: -" : $"Fill: {(string.IsNullOrEmpty(fill) ? "none" : fill)}");

        _targetToggle.SetText(_target == ChipTarget.Inventory ? "Target: Inventory" : "Target: Reward");

        foreach (var rule in ExposedRules)
            _ruleButtons[rule].SetText(
                $"{ColoringRules.ShortName(rule)}: {(_view.EditorHasRule(rule) ? "ON" : "off")}");

        HandleChipClicks();
    }

    /// <summary>Left-click a chip to add one of that rune, right-click to remove one.</summary>
    private void HandleChipClicks()
    {
        var mouse = Mouse.GetState();
        bool left = mouse.LeftButton == ButtonState.Pressed;
        bool right = mouse.RightButton == ButtonState.Pressed;
        bool leftClick = left && !_prevLeft;
        bool rightClick = right && !_prevRight;
        _prevLeft = left;
        _prevRight = right;

        // Only the active editor (edit mode) drives the grid.
        if (!_view.EditMode || (!leftClick && !rightClick)) return;

        var mp = Core.GetTransformedMousePoint();
        foreach (var (name, rect) in PyramidSlots())
        {
            if (!rect.Contains(mp)) continue;
            ApplyChip(name, leftClick ? 1 : -1);
            return;
        }
    }

    public override void Draw(SpriteBatch sb)
    {
        sb.Draw(_pixel, _bounds, null, ColorPalette.White, 0f, Vector2.Zero, SpriteEffects.None, 0.5f);
        sb.Draw(_pixel, new Rectangle(_bounds.X, _bounds.Y, 2, _bounds.Height), null, ColorPalette.Black,
            0f, Vector2.Zero, SpriteEffects.None, 0.51f);

        sb.DrawString(_font, "Node", _headerPos, ColorPalette.Black);

        var puzId = _view.SelectedPuzzleId;
        sb.DrawString(_font, puzId == null ? "Puzzle" : $"Puzzle: {puzId}", _puzHeaderPos, ColorPalette.Black);
        sb.DrawString(_font, $"Order: {_view.SelectedPuzzleOrder}", _puzOrderPos, ColorPalette.Black);

        sb.DrawString(_font, "Level", _levelHeaderPos, ColorPalette.Black);

        string hint = _target == ChipTarget.Inventory
            ? "Inventory: L-click +1, R-click -1"
            : puzId == null ? "Reward: select a node first" : $"Reward for {puzId}: L +1, R -1";
        sb.DrawString(_font, hint, _gridHintPos, Color.Gray, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0.49f);

        DrawPyramid(sb);

        sb.DrawString(_font, "Rules", _rulesLabelPos, ColorPalette.Black);

        base.Draw(sb);
    }

    private void DrawPyramid(SpriteBatch sb)
    {
        var slots = PyramidSlots();
        DrawPyramidGuides(sb, slots);
        foreach (var (name, rect) in slots)
            DrawChip(sb, rect, name, ChipCount(name));
    }

    /// <summary>Faint guides behind the pyramid: a vertical centre line (the left/right "sidedness"
    /// divide) and a horizontal line between each tier row. Mirrors the play view.</summary>
    private void DrawPyramidGuides(SpriteBatch sb, List<(string name, Rectangle rect)> slots)
    {
        if (slots.Count == 0) return;
        var gray = Color.DimGray;

        var rows = new List<List<Rectangle>>();
        int curY = int.MinValue;
        foreach (var (_, r) in slots)
        {
            if (r.Y != curY) { rows.Add(new List<Rectangle>()); curY = r.Y; }
            rows[^1].Add(r);
        }

        int top = rows[0][0].Top;
        int bottom = rows[^1][0].Bottom;
        int centerX = (rows[^1][0].Left + rows[^1][^1].Right) / 2;

        sb.Draw(_pixel, new Rectangle(centerX - 1, top, 3, bottom - top), null, gray,
            0f, Vector2.Zero, SpriteEffects.None, 0.47f);

        for (int i = 0; i < rows.Count - 1; i++)
        {
            var lower = rows[i + 1];
            int y = (rows[i][0].Bottom + lower[0].Top) / 2;
            sb.Draw(_pixel, new Rectangle(lower[0].Left, y - 1, lower[^1].Right - lower[0].Left, 3), null, gray,
                0f, Vector2.Zero, SpriteEffects.None, 0.47f);
        }
    }

    /// <summary>Draws one pyramid chip, matching the play view: owned (count &gt; 0) is a filled black
    /// disc + white glyph + a small badge; count 0 is inverted — a white outlined ring + black glyph.</summary>
    private void DrawChip(SpriteBatch sb, Rectangle rect, string rune, int count)
    {
        bool owned = count > 0;
        var center = rect.Center.ToVector2();

        float discSize = rect.Width * 0.95f;
        var discDst = new Rectangle((int)(center.X - discSize / 2), (int)(center.Y - discSize / 2), (int)discSize, (int)discSize);
        sb.Draw(owned ? _circleFilled : _circleOutlined, discDst, null,
            owned ? ColorPalette.Black : ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.46f);

        var glyphColor = owned ? ColorPalette.ActualWhite : ColorPalette.Black;
        var tex = Runes.Texture(rune);
        float s = rect.Width * 0.7f;
        var idst = new Rectangle((int)(center.X - s / 2), (int)(center.Y - s / 2), (int)s, (int)s);
        if (tex != null)
            sb.Draw(tex, idst, null, glyphColor, 0f, Vector2.Zero, SpriteEffects.None, 0.45f);
        else
        {
            var size = _font.MeasureString(rune) * 0.6f;
            sb.DrawString(_font, rune, center - size / 2f, glyphColor, 0f, Vector2.Zero, 0.6f, SpriteEffects.None, 0.45f);
        }

        if (owned)
        {
            const float bscale = 0.6f;
            var badge = "x" + count;
            var bsize = _font.MeasureString(badge) * bscale;
            var bpos = new Vector2(rect.Right - bsize.X - 3, rect.Bottom - bsize.Y - 2);
            sb.Draw(_pixel, new Rectangle((int)bpos.X - 2, (int)bpos.Y - 1, (int)bsize.X + 4, (int)bsize.Y + 1),
                null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.44f);
            sb.DrawString(_font, badge, bpos, ColorPalette.ActualWhite, 0f, Vector2.Zero, bscale, SpriteEffects.None, 0.43f);
        }
    }

    public override void OnRemovedFromUI() => _pixel?.Dispose();
}
