using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

/// <summary>
/// Renders flowing rich text with inline interactive slots, highlights, and links.
/// Slots are declared in the template string as {n} and are clickable fill-in-the-blank gaps.
/// </summary>
public class RichTextArea : UIElement
{
    // ── Public types ─────────────────────────────────────────────────────────

    public class Slot
    {
        public InfoType InfoType { get; set; }
        public string Value { get; set; }              // null = unfilled
        public string CorrectSolution { get; set; }
        public string[] AcceptableSolutions { get; set; }
    }

    // ── Private layout types ─────────────────────────────────────────────────

    private enum TokenKind { Word, Slot, Break }
    private struct Token { public TokenKind Kind; public string Text; public int SlotIndex; }

    private enum AtomKind { Text, Slot }
    private struct Atom
    {
        public AtomKind Kind;
        public string Text;
        public int SlotIndex;
        public float X, W;
        public Color Color;
    }

    private record HighlightDef(string MatchText, Color Color);
    private record LinkDef(string MatchText, Color Color, Action OnClick);

    // ── Fields ───────────────────────────────────────────────────────────────

    private Rectangle _bounds;
    private readonly SpriteFont _font;
    private Color _bgColor;
    private Color _fgColor;
    private Texture2D _pixel;

    private const int Pad = 12;
    private const int ScrollbarW = 16;
    private const float SlotMinW = 180f;

    private float _lineHeight;

    private string _template = "";
    private List<Slot> _slots = new();
    private List<List<Atom>> _lines = new();

    private List<HighlightDef> _highlights = new();
    private List<LinkDef> _links = new();
    private List<(Rectangle Bounds, Action OnClick)> _linkHitAreas = new();
    private int _hoveredLink = -1;

    private int _scrollOffset;
    private int _maxVisible;
    private Slider _scrollbar;
    private bool _scrollbarVisible;
    private bool _syncingScrollbar;
    private Action<float> _scrollbarHandler;

    private int _hoveredSlot = -1;
    private MouseState _prevMouse;

    // slotIndex, infoType, onValueSelected(string)
    public Action<int, InfoType, Action<string>> OnSlotClicked;

    // ── Constructor ──────────────────────────────────────────────────────────

    public RichTextArea(Rectangle bounds, SpriteFont font, Color? bg = null, Color? fg = null)
    {
        _bounds = bounds;
        _font = font;
        _lineHeight = font.LineSpacing;
        _bgColor = bg ?? Color.White;
        _fgColor = fg ?? Color.Black;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _prevMouse = Mouse.GetState();

        _scrollbarHandler = v =>
        {
            int max = Math.Max(0, _lines.Count - _maxVisible);
            _scrollOffset = (int)((1f - v) * max);
        };

        InitScrollbar();
        Rebuild();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void SetContent(string template, List<Slot> slots)
    {
        _template = template ?? "";
        _slots = slots ?? new List<Slot>();
        _scrollOffset = 0;
        Rebuild();
    }

    public void Refresh() => Rebuild();
    public List<Slot> GetSlots() => _slots;

    public void AddHighlight(string matchText, Color color) => _highlights.Add(new(matchText, color));
    public void ClearHighlights() { _highlights.Clear(); }

    public void AddLink(string matchText, Color color, Action onClick) => _links.Add(new(matchText, color, onClick));
    public void ClearLinks() { _links.Clear(); }

    // ── Tokenize ─────────────────────────────────────────────────────────────

    private List<Token> Tokenize()
    {
        var result = new List<Token>();
        var rawLines = _template.Replace("\r\n", "\n").Split('\n');

        for (int li = 0; li < rawLines.Length; li++)
        {
            if (li > 0) result.Add(new Token { Kind = TokenKind.Break });
            string line = rawLines[li];
            int i = 0;

            while (i < line.Length)
            {
                if (line[i] == '{')
                {
                    int close = line.IndexOf('}', i + 1);
                    if (close > i && int.TryParse(line.Substring(i + 1, close - i - 1).Trim(), out int idx))
                    {
                        result.Add(new Token { Kind = TokenKind.Slot, SlotIndex = idx });
                        i = close + 1;
                        continue;
                    }
                }

                int end = line.IndexOf('{', i);
                string span = end < 0 ? line.Substring(i) : line.Substring(i, end - i);
                i = end < 0 ? line.Length : end;

                foreach (var w in span.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    result.Add(new Token { Kind = TokenKind.Word, Text = w });
            }
        }

        return result;
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private Rectangle GetTextBounds()
    {
        int sbW = _scrollbarVisible ? ScrollbarW : 0;
        return new Rectangle(_bounds.X + Pad, _bounds.Y + Pad,
            _bounds.Width - Pad * 2 - sbW, _bounds.Height - Pad * 2);
    }

    private void Rebuild()
    {
        _lines.Clear();
        _linkHitAreas.Clear();

        var tb = GetTextBounds();
        float maxW = tb.Width;
        float spaceW = _font.MeasureString(" ").X;

        var cur = new List<Atom>();
        float x = 0f;

        void Flush()
        {
            _lines.Add(new List<Atom>(cur));
            cur.Clear();
            x = 0f;
        }

        foreach (var tok in Tokenize())
        {
            if (tok.Kind == TokenKind.Break) { Flush(); continue; }

            bool attaches = tok.Kind == TokenKind.Word && tok.Text.Length > 0
                            && ".,!?;:)".IndexOf(tok.Text[0]) >= 0;

            float gap = (x == 0f || attaches) ? 0f : spaceW;
            float w   = tok.Kind == TokenKind.Slot
                ? Math.Max(SlotMinW, _font.MeasureString(_slots.Count > tok.SlotIndex && _slots[tok.SlotIndex].Value != null
                    ? _slots[tok.SlotIndex].Value : "___________").X + 20f)
                : _font.MeasureString(tok.Text).X;

            if (x + gap + w > maxW && cur.Count > 0) { Flush(); gap = 0f; }

            Color atomColor = ResolveTextColor(tok.Kind == TokenKind.Word ? tok.Text : null);
            cur.Add(tok.Kind == TokenKind.Slot
                ? new Atom { Kind = AtomKind.Slot, SlotIndex = tok.SlotIndex, X = x + gap, W = w }
                : new Atom { Kind = AtomKind.Text, Text = tok.Text, X = x + gap, W = w, Color = atomColor });

            x += gap + w;
        }

        if (cur.Count > 0) Flush();

        CalcMaxVisible();
        RefreshScrollbar();
    }

    private Color ResolveTextColor(string word)
    {
        if (word == null) return _fgColor;
        foreach (var h in _highlights)
            if (word.Equals(h.MatchText, StringComparison.OrdinalIgnoreCase))
                return h.Color;
        foreach (var l in _links)
            if (word.Equals(l.MatchText, StringComparison.OrdinalIgnoreCase))
                return l.Color;
        return _fgColor;
    }

    private void CalcMaxVisible()
    {
        _maxVisible = (int)Math.Floor(GetTextBounds().Height / _lineHeight);
    }

    // ── Scrollbar ────────────────────────────────────────────────────────────

    private void InitScrollbar()
    {
        _scrollbar = new Slider(new Rectangle(0, 0, ScrollbarW, 100),
            0f, 1f, 0f, 0f, false,
            Color.DarkGray, Color.Transparent, Color.Gray, Color.LightGray, Color.DimGray,
            ScrollbarW - 4, ScrollbarW - 2, 1);
        _scrollbar.OnValueChanged += _scrollbarHandler;
        _scrollbar.LocalOrderOffset = 0.05f;
    }

    private void RefreshScrollbar()
    {
        if (_syncingScrollbar) return;
        _syncingScrollbar = true;
        try
        {
            bool needs = _lines.Count > _maxVisible;
            if (needs != _scrollbarVisible)
            {
                _scrollbarVisible = needs;
                CalcMaxVisible();
                needs = _lines.Count > _maxVisible;
                _scrollbarVisible = needs;
                if (_scrollbarVisible) Rebuild();
            }
            if (_scrollbarVisible)
            {
                _scrollbar.SetBounds(new Rectangle(_bounds.Right - ScrollbarW, _bounds.Y, ScrollbarW, _bounds.Height));
                _scrollbar.SetVisibility(true);
                _scrollbar.Order = Order + 0.1f;
                SyncScrollbar();
            }
        }
        finally { _syncingScrollbar = false; }
    }

    private void SyncScrollbar()
    {
        if (!_scrollbarVisible) return;
        int max = Math.Max(0, _lines.Count - _maxVisible);
        _scrollbar.OnValueChanged -= _scrollbarHandler;
        _scrollbar.Value = max > 0 ? 1f - (float)_scrollOffset / max : 1f;
        _scrollbar.OnValueChanged += _scrollbarHandler;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    private bool IsParentWindowFocused()
    {
        var focused = Core.UISystem.WindowManager.FocusedWindow;
        return focused == null || focused == GetRoot();
    }

    public override void Update(float deltaTime)
    {
        if (!IsVisible()) return;

        var mouse = Mouse.GetState();
        var mp = Core.GetTransformedMousePoint();
        var tb = GetTextBounds();

        if (_bounds.Contains(mp) && IsParentWindowFocused())
        {
            int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
            if (wheel != 0)
            {
                int max = Math.Max(0, _lines.Count - _maxVisible);
                _scrollOffset = (int)Math.Clamp(_scrollOffset + (wheel > 0 ? -3 : 3), 0, max);
                SyncScrollbar();
            }
        }

        if (_scrollbarVisible && IsParentWindowFocused()) _scrollbar.Update(deltaTime);

        _hoveredSlot = -1;
        _hoveredLink = -1;

        if (tb.Contains(mp) && IsParentWindowFocused())
        {
            for (int li = 0; li < _lines.Count; li++)
            {
                int di = li - _scrollOffset;
                if (di < 0) continue;
                if (di >= _maxVisible) break;
                float ly = tb.Y + di * _lineHeight;

                foreach (var atom in _lines[li])
                {
                    var r = new Rectangle((int)(tb.X + atom.X), (int)ly, (int)atom.W, (int)_lineHeight);
                    if (!r.Contains(mp)) continue;

                    if (atom.Kind == AtomKind.Slot) _hoveredSlot = atom.SlotIndex;
                    else
                    {
                        for (int k = 0; k < _links.Count; k++)
                            if (atom.Text.Equals(_links[k].MatchText, StringComparison.OrdinalIgnoreCase))
                            { _hoveredLink = k; break; }
                    }
                }
            }
        }

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if (clicked && IsParentWindowFocused())
        {
            if (_hoveredSlot >= 0 && _hoveredSlot < _slots.Count)
            {
                int idx = _hoveredSlot;
                var slot = _slots[idx];
                OnSlotClicked?.Invoke(idx, slot.InfoType, val => { slot.Value = val; Rebuild(); });
            }
            else if (_hoveredLink >= 0)
            {
                _links[_hoveredLink].OnClick?.Invoke();
            }
        }

        _prevMouse = mouse;
        base.Update(deltaTime);
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb)
    {
        if (!IsVisible()) return;

        sb.Draw(_pixel, _bounds, null, _bgColor, 0, Vector2.Zero, SpriteEffects.None, GetActualOrder());

        var tb = GetTextBounds();

        sb.End();
        var rasterizer = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        var origScissor = Core.GraphicsDevice.ScissorRectangle;
        Core.GraphicsDevice.ScissorRectangle = tb;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer);

        for (int li = 0; li < _lines.Count; li++)
        {
            int di = li - _scrollOffset;
            if (di < 0) continue;
            if (di >= _maxVisible) break;
            float ly = tb.Y + di * _lineHeight;

            foreach (var atom in _lines[li])
            {
                float ax = tb.X + atom.X;

                if (atom.Kind == AtomKind.Text)
                {
                    Color col = atom.Color;
                    // Brighten hovered link
                    if (_hoveredLink >= 0 && atom.Text.Equals(_links[_hoveredLink].MatchText, StringComparison.OrdinalIgnoreCase))
                        col = Color.Lerp(col, Color.White, 0.4f);
                    sb.DrawString(_font, atom.Text, new Vector2(ax, ly), col);
                }
                else
                {
                    DrawSlot(sb, atom, ax, ly);
                }
            }
        }

        sb.End();
        Core.GraphicsDevice.ScissorRectangle = origScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

        if (_scrollbarVisible) _scrollbar.Draw(sb);
        base.Draw(sb);
    }

    private void DrawSlot(SpriteBatch sb, Atom atom, float ax, float ly)
    {
        var slot = atom.SlotIndex < _slots.Count ? _slots[atom.SlotIndex] : null;
        bool filled = slot != null && !string.IsNullOrEmpty(slot.Value);
        bool hovered = _hoveredSlot == atom.SlotIndex;

        var slotRect = new Rectangle((int)ax, (int)ly + 2, (int)atom.W, (int)_lineHeight - 4);

        Color bg = filled
            ? Color.Lerp(ColorPalette.DarkGreen, Color.White, hovered ? 0.2f : 0f)
            : Color.Lerp(ColorPalette.LightGreen, Color.Black, hovered ? 0.1f : 0.05f);

        sb.Draw(_pixel, slotRect, bg);

        // Bottom underline on empty slots
        if (!filled)
            sb.Draw(_pixel, new Rectangle(slotRect.X, slotRect.Bottom - 2, slotRect.Width, 2), ColorPalette.DarkGreen);

        string display = filled ? slot.Value : string.Empty;
        Color textCol = filled ? ColorPalette.ActualWhite : ColorPalette.DarkGreen;

        if (!string.IsNullOrEmpty(display))
        {
            var ts = _font.MeasureString(display);
            float tx = ax + Math.Max(4, (atom.W - ts.X) / 2f);
            float ty = ly + (_lineHeight - ts.Y) / 2f;
            sb.DrawString(_font, display, new Vector2(tx, ty), textCol);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        Rebuild();
    }
}
