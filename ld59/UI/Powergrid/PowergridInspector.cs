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
/// Right-hand editor inspector. Shows a different top section depending on what is selected:
///   • Node selected   → Node + Puzzle sections
///   • Connection selected → Connection rules section
///   • Region selected → Region (tier / max-count) section
///   • Nothing selected → (no top section)
/// The Level section (inventory, adjacency rules) is always shown at the bottom.
///
/// The inventory/reward editor reuses the play-mode <b>alphabet pyramid</b>: every rune is a chip
/// laid out by tier/side, showing its current count. Left-click a chip to add one, right-click to
/// remove one. A target toggle picks whether the chips edit the level inventory or the selected
/// puzzle's reward.
/// </summary>
public sealed class PowergridInspector : UIPanel
{
    /// <summary>Rules surfaced as editor toggles, in display order.</summary>
    private static readonly ColoringRule[] ExposedRules =
    {
        ColoringRule.DifferentRune, ColoringRule.TierStep, ColoringRule.Sidedness,
    };

    /// <summary>All four rules are available as per-connection overrides (including DifferentTier).</summary>
    private static readonly ColoringRule[] OverrideRules =
    {
        ColoringRule.DifferentRune, ColoringRule.DifferentTier, ColoringRule.TierStep, ColoringRule.Sidedness,
    };

    private enum ChipTarget { Inventory, Reward }

    private const int ChipGap = 6;

    private Rectangle _bounds;
    private readonly PowergridView _view;
    private readonly SpriteFont _font;
    private readonly Texture2D _pixel;
    private readonly Texture2D _circleFilled;
    private readonly Texture2D _circleOutlined;

    // ── Node section ──────────────────────────────────────────────────────
    private readonly Button _fixedRune, _deleteNode;
    private readonly Button _influenceMinus, _influencePlus;

    // ── Puzzle section ────────────────────────────────────────────────────
    private readonly Button _puzOrderMinus, _puzOrderPlus;

    // ── Connection section ────────────────────────────────────────────────
    private readonly Dictionary<ColoringRule, Button> _connRuleButtons = new();
    private readonly Button _clearOverride;

    // ── Region section ────────────────────────────────────────────────────
    private readonly Button _regionTierMinus, _regionTierPlus;
    private readonly Button _regionMaxMinus, _regionMaxPlus;
    private readonly Button _deleteRegion;

    // ── Text section ──────────────────────────────────────────────────────
    private readonly TextArea _textField;
    private readonly Button _textScaleMinus, _textScalePlus;
    private readonly Button _textAlign, _deleteText;
    /// <summary>The label the text field is currently bound to, so we only push the field's contents
    /// into it (and only re-fill the field) when the selection actually changes.</summary>
    private PowergridTextComponent _boundLabel;

    // ── Level section ─────────────────────────────────────────────────────
    private readonly Button _targetToggle;
    private readonly Dictionary<ColoringRule, Button> _ruleButtons = new();

    private ChipTarget _target = ChipTarget.Inventory;

    private Rectangle _gridArea;
    private bool _prevLeft, _prevRight;

    // Section header / label positions, recomputed in SetBounds.
    private Vector2 _topSectionHeaderPos;
    private Vector2 _influencePos;
    private Vector2 _puzHeaderPos, _puzOrderPos;
    private Vector2 _regionTierPos, _regionMaxPos;
    private Vector2 _textScalePos;
    private Vector2 _connHeaderPos;
    private Vector2 _levelHeaderPos, _gridHintPos, _rulesLabelPos;

    // Track selection type to re-layout when it changes.
    private enum SelectionKind { None, Node, Connection, Region, Text }
    private SelectionKind _lastKind = SelectionKind.None;

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

        // Node section
        _fixedRune     = Mk("Fill", _view.CycleFixedRune);
        _deleteNode    = Mk("Delete Node", _view.DeleteSelected);
        _influenceMinus = Mk("-", () => _view.AdjustInfluence(-1));
        _influencePlus  = Mk("+", () => _view.AdjustInfluence(1));
        AddChild(_fixedRune);
        AddChild(_deleteNode);
        AddChild(_influenceMinus);
        AddChild(_influencePlus);

        // Puzzle section
        _puzOrderMinus = Mk("-", () => _view.AdjustPuzzleOrder(-1));
        _puzOrderPlus  = Mk("+", () => _view.AdjustPuzzleOrder(1));
        AddChild(_puzOrderMinus);
        AddChild(_puzOrderPlus);

        // Connection section
        foreach (var rule in OverrideRules)
        {
            var captured = rule;
            var btn = Mk(ColoringRules.ShortName(rule), () => _view.ToggleConnectionRule(captured));
            _connRuleButtons[rule] = btn;
            AddChild(btn);
        }
        _clearOverride = Mk("Clear Override", _view.ClearConnectionOverride);
        AddChild(_clearOverride);

        // Region section
        _regionTierMinus = Mk("-", () => _view.AdjustRegionTier(-1));
        _regionTierPlus  = Mk("+", () => _view.AdjustRegionTier(1));
        _regionMaxMinus  = Mk("-", () => _view.AdjustRegionMax(-1));
        _regionMaxPlus   = Mk("+", () => _view.AdjustRegionMax(1));
        _deleteRegion    = Mk("Delete Region", _view.DeleteSelectedRegion);
        AddChild(_regionTierMinus);
        AddChild(_regionTierPlus);
        AddChild(_regionMaxMinus);
        AddChild(_regionMaxPlus);
        AddChild(_deleteRegion);

        // Text section
        _textField = new TextArea(new Rectangle(0, 0, 10, 10), _font,
            backgroundColor: ColorPalette.ActualWhite, textColor: ColorPalette.Black,
            borderColor: ColorPalette.Black, focusedBorderColor: ColorPalette.DarkGreen);
        _textField.OnTextChanged += t => _view.SetSelectedTextContent(t);
        _textScaleMinus = Mk("-", () => _view.AdjustTextScale(-0.25f));
        _textScalePlus  = Mk("+", () => _view.AdjustTextScale(0.25f));
        _textAlign      = Mk("Align: Center", _view.CycleTextAlign);
        _deleteText     = Mk("Delete Text", _view.DeleteSelectedText);
        AddChild(_textField);
        AddChild(_textScaleMinus);
        AddChild(_textScalePlus);
        AddChild(_textAlign);
        AddChild(_deleteText);

        // Level section
        _targetToggle = Mk("Target: Inventory",
            () => _target = _target == ChipTarget.Inventory ? ChipTarget.Reward : ChipTarget.Inventory);
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

    private SelectionKind CurrentKind()
    {
        if (_view.SelectedConnection != null) return SelectionKind.Connection;
        if (_view.SelectedRegion    != null) return SelectionKind.Region;
        if (_view.SelectedText      != null) return SelectionKind.Text;
        if (_view.Selected          != null) return SelectionKind.Node;
        return SelectionKind.None;
    }

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        const int pad = 8, h = 30, gap = 6;
        int x = _bounds.X + pad;
        int w = _bounds.Width - 2 * pad;
        int y = _bounds.Y + pad;

        var kind = CurrentKind();

        // ── Top section (selection-dependent) ────────────────────────────
        _topSectionHeaderPos = new Vector2(x, y);

        if (kind == SelectionKind.Node)
        {
            y += 26;
            _fixedRune.SetBounds(new Rectangle(x, y, w, h)); y += h + gap;
            // Influence row
            _influencePos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
            _influencePlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
            _influenceMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
            y += h + gap;
            _deleteNode.SetBounds(new Rectangle(x, y, w, h)); y += h + gap + 14;

            _puzHeaderPos = new Vector2(x, y); y += 26;
            _puzOrderPos  = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
            _puzOrderPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
            _puzOrderMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
            y += h + gap + 14;
        }
        else if (kind == SelectionKind.Connection)
        {
            _connHeaderPos = new Vector2(x, y); y += 26;
            foreach (var rule in OverrideRules)
            {
                _connRuleButtons[rule].SetBounds(new Rectangle(x, y, w, h));
                y += h + gap;
            }
            _clearOverride.SetBounds(new Rectangle(x, y, w, h));
            y += h + gap + 14;
        }
        else if (kind == SelectionKind.Region)
        {
            y += 26;
            // Tier row
            _regionTierPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
            _regionTierPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
            _regionTierMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
            y += h + gap;
            // Max row
            _regionMaxPos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
            _regionMaxPlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
            _regionMaxMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
            y += h + gap;
            _deleteRegion.SetBounds(new Rectangle(x, y, w, h));
            y += h + gap + 14;
        }
        else if (kind == SelectionKind.Text)
        {
            y += 26;
            _textField.SetBounds(new Rectangle(x, y, w, 96)); y += 96 + gap;
            // Scale row
            _textScalePos = new Vector2(x, y + (h - _font.LineSpacing) / 2f);
            _textScalePlus.SetBounds(new Rectangle(_bounds.Right - pad - h, y, h, h));
            _textScaleMinus.SetBounds(new Rectangle(_bounds.Right - pad - h * 2 - 4, y, h, h));
            y += h + gap;
            _textAlign.SetBounds(new Rectangle(x, y, w, h)); y += h + gap;
            _deleteText.SetBounds(new Rectangle(x, y, w, h));
            y += h + gap + 14;
        }

        // ── Level section (always at bottom) ─────────────────────────────
        _levelHeaderPos = new Vector2(x, y); y += 26;
        _targetToggle.SetBounds(new Rectangle(x, y, w, h)); y += h + gap;
        _gridHintPos = new Vector2(x, y); y += 20;

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

        // Re-layout when selection type changes.
        var kind = CurrentKind();
        if (kind != _lastKind) { _lastKind = kind; SetBounds(_bounds); }

        // ── Show/hide buttons by section ──────────────────────────────────
        bool nodeSelected = kind == SelectionKind.Node;
        bool connSelected = kind == SelectionKind.Connection;
        bool regionSelected = kind == SelectionKind.Region;
        bool textSelected = kind == SelectionKind.Text;

        _fixedRune.SetEnabled(nodeSelected);
        _deleteNode.SetEnabled(nodeSelected);
        _influenceMinus.SetEnabled(nodeSelected);
        _influencePlus.SetEnabled(nodeSelected);
        _puzOrderMinus.SetEnabled(nodeSelected);
        _puzOrderPlus.SetEnabled(nodeSelected);
        _fixedRune.SetVisibility(nodeSelected);
        _deleteNode.SetVisibility(nodeSelected);
        _influenceMinus.SetVisibility(nodeSelected);
        _influencePlus.SetVisibility(nodeSelected);
        _puzOrderMinus.SetVisibility(nodeSelected);
        _puzOrderPlus.SetVisibility(nodeSelected);

        foreach (var (_, btn) in _connRuleButtons) btn.SetVisibility(connSelected);
        _clearOverride.SetVisibility(connSelected);
        _clearOverride.SetEnabled(connSelected && _view.ConnectionHasOverride);

        _regionTierMinus.SetVisibility(regionSelected);
        _regionTierPlus.SetVisibility(regionSelected);
        _regionMaxMinus.SetVisibility(regionSelected);
        _regionMaxPlus.SetVisibility(regionSelected);
        _deleteRegion.SetVisibility(regionSelected);
        _regionTierMinus.SetEnabled(regionSelected);
        _regionTierPlus.SetEnabled(regionSelected);
        _regionMaxMinus.SetEnabled(regionSelected);
        _regionMaxPlus.SetEnabled(regionSelected);
        _deleteRegion.SetEnabled(regionSelected);

        _textField.SetVisibility(textSelected);
        foreach (var btn in new[] { _textScaleMinus, _textScalePlus, _textAlign, _deleteText })
        {
            btn.SetVisibility(textSelected);
            btn.SetEnabled(textSelected);
        }

        // Bind the text field to whichever label is selected (only on a change, so typing isn't
        // clobbered every frame).
        if (_view.SelectedText != _boundLabel)
        {
            _boundLabel = _view.SelectedText;
            _textField.Text = _boundLabel?.Text ?? string.Empty;
            _textField.SetFocus(textSelected);
        }

        if (textSelected)
            _textAlign.SetText($"Align: {_view.SelectedTextAlign}");

        // Update dynamic labels
        if (nodeSelected)
        {
            var s = _view.Selected;
            var fill = _view.SelectedFixedRune;
            _fixedRune.SetText(s == null ? "Fill: -" : $"Fill: {(string.IsNullOrEmpty(fill) ? "none" : fill)}");
        }

        if (connSelected)
        {
            foreach (var rule in OverrideRules)
            {
                bool active = _view.ConnectionOverrideHasRule(rule);
                bool hasOverride = _view.ConnectionHasOverride;
                string status = !hasOverride ? "level default" : active ? "ON" : "off";
                _connRuleButtons[rule].SetText($"{ColoringRules.ShortName(rule)}: {status}");
            }
        }

        _targetToggle.SetText(_target == ChipTarget.Inventory ? "Target: Inventory" : "Target: Reward");

        foreach (var rule in ExposedRules)
            _ruleButtons[rule].SetText(
                $"{ColoringRules.ShortName(rule)}: {(_view.EditorHasRule(rule) ? "ON" : "off")}");

        HandleChipClicks();
    }

    private void HandleChipClicks()
    {
        var mouse = Mouse.GetState();
        bool left = mouse.LeftButton == ButtonState.Pressed;
        bool right = mouse.RightButton == ButtonState.Pressed;
        bool leftClick = left && !_prevLeft;
        bool rightClick = right && !_prevRight;
        _prevLeft = left;
        _prevRight = right;

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

        var kind = CurrentKind();

        if (kind == SelectionKind.Node)
        {
            sb.DrawString(_font, "Node", _topSectionHeaderPos, ColorPalette.Black);
            sb.DrawString(_font, $"Influence: {_view.SelectedInfluence}", _influencePos, ColorPalette.Black);

            var puzId = _view.SelectedPuzzleId;
            sb.DrawString(_font, puzId == null ? "Puzzle" : $"Puzzle: {puzId}", _puzHeaderPos, ColorPalette.Black);
            sb.DrawString(_font, $"Order: {_view.SelectedPuzzleOrder}", _puzOrderPos, ColorPalette.Black);
        }
        else if (kind == SelectionKind.Connection)
        {
            sb.DrawString(_font, "Connection", _connHeaderPos, ColorPalette.Black);
            var overrideStatus = _view.ConnectionHasOverride ? "override active" : "using level rules";
            sb.DrawString(_font, overrideStatus, _connHeaderPos + new Vector2(0, _font.LineSpacing),
                Color.DimGray, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0.49f);
        }
        else if (kind == SelectionKind.Region)
        {
            sb.DrawString(_font, "Region", _topSectionHeaderPos, ColorPalette.Black);
            var r = _view.SelectedRegion;
            if (r != null)
            {
                sb.DrawString(_font, $"Tier: {r.Tier}", _regionTierPos, ColorPalette.Black);
                sb.DrawString(_font, $"Max: {r.MaxCount}", _regionMaxPos, ColorPalette.Black);
            }
        }

        else if (kind == SelectionKind.Text)
        {
            sb.DrawString(_font, "Text", _topSectionHeaderPos, ColorPalette.Black);
            sb.DrawString(_font, $"Scale: {_view.SelectedTextScale:0.##}", _textScalePos, ColorPalette.Black);
        }

        sb.DrawString(_font, "Level", _levelHeaderPos, ColorPalette.Black);

        var puzIdForHint = _view.SelectedPuzzleId;
        string hint = _target == ChipTarget.Inventory
            ? "Inventory: L-click +1, R-click -1"
            : puzIdForHint == null ? "Reward: select a node first" : $"Reward for {puzIdForHint}: L +1, R -1";
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
