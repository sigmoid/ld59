using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

public class BrowserTextArea : UIElement
{
    private Rectangle _bounds;
    private SpriteFont _font;
    private string _text = string.Empty;
    private List<string> _origLines = new();
    private List<string> _wrappedLines = new();
    private List<(int OrigLine, int CharStart)> _wrapMeta = new();
    private float _lineHeight;
    private int _scrollOffset;
    private int _maxVisible;
    private const int Pad = 10;
    private Color _bgColor;
    private Color _fgColor;
    private Texture2D _pixel;

    private record LinkDef(string MatchText, Color Color, Action OnClick);
    private List<LinkDef> _linkDefs = new();
    private record HighlightDef(string MatchText, Color Color);
    private List<HighlightDef> _highlightDefs = new();
    private List<(Rectangle Area, Action OnClick, Color Color)> _hitAreas = new();
    private bool _hitsDirty = true;
    private int _hoveredHitIndex = -1;

    private Slider _scrollbar;
    private bool _scrollbarVisible;
    private bool _syncingScrollbar;
    private const int ScrollbarW = 16;

    private Action<float> _scrollbarHandler;
    private MouseState _prevMouse;

    public string Text { get => _text; set { _text = value ?? ""; RefreshLines(); } }

    public BrowserTextArea(Rectangle bounds, SpriteFont font, Color? bg = null, Color? fg = null)
    {
        _bounds = bounds;
        _font = font;
        _bgColor = bg ?? Color.White;
        _fgColor = fg ?? Color.Black;
        _lineHeight = font.LineSpacing;
        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _prevMouse = Mouse.GetState();
        _scrollbarHandler = v =>
        {
            int max = Math.Max(0, _wrappedLines.Count - _maxVisible);
            _scrollOffset = (int)((1f - v) * max);
            _hitsDirty = true;
        };
        InitScrollbar();
        RefreshLines();
    }

    public void AddLink(string matchText, Color color, Action onClick)
    {
        _linkDefs.Add(new LinkDef(matchText, color, onClick));
        _hitsDirty = true;
    }

    public void ClearLinks()
    {
        _linkDefs.Clear();
        _hitsDirty = true;
    }

    public void AddHighlight(string matchText, Color color)
    {
        _highlightDefs.Add(new HighlightDef(matchText, color));
        _hitsDirty = true;
    }

    public void ClearHighlights()
    {
        _highlightDefs.Clear();
        _hitsDirty = true;
    }

    private void RefreshLines()
    {
        _scrollOffset = 0;
        _origLines.Clear();
        var normalized = _text.Replace("\r\n", "\n").Replace("\r", "\n");
        _origLines.AddRange(normalized.Split('\n'));
        RebuildWrappedLines();
    }

    private void RebuildWrappedLines()
    {
        _wrappedLines.Clear();
        _wrapMeta.Clear();

        var tb = GetTextBounds();
        float maxW = tb.Width;

        for (int i = 0; i < _origLines.Count; i++)
        {
            string line = _origLines[i];
            if (string.IsNullOrEmpty(line))
            {
                _wrappedLines.Add(string.Empty);
                _wrapMeta.Add((i, 0));
                continue;
            }
            var segs = WrapLine(line, maxW);
            int charPos = 0;
            foreach (var seg in segs)
            {
                _wrappedLines.Add(seg);
                _wrapMeta.Add((i, charPos));
                charPos += seg.Length;
                if (charPos < line.Length && line[charPos] == ' ') charPos++;
            }
        }

        CalcMaxVisible();
        RefreshScrollbar();
        _hitsDirty = true;
    }

    private List<string> WrapLine(string line, float maxW)
    {
        var result = new List<string>();
        if (_font.MeasureString(line).X <= maxW) { result.Add(line); return result; }

        var words = line.Split(' ');
        var current = string.Empty;
        foreach (var word in words)
        {
            var test = string.IsNullOrEmpty(current) ? word : current + " " + word;
            if (_font.MeasureString(test).X <= maxW)
            {
                current = test;
            }
            else
            {
                if (!string.IsNullOrEmpty(current)) { result.Add(current); current = word; }
                else
                {
                    var part = string.Empty;
                    foreach (char c in word)
                    {
                        var tp = part + c;
                        if (_font.MeasureString(tp).X <= maxW) part = tp;
                        else { if (!string.IsNullOrEmpty(part)) result.Add(part); part = c.ToString(); }
                    }
                    current = part;
                }
            }
        }
        if (!string.IsNullOrEmpty(current)) result.Add(current);
        return result;
    }

    private void CalcMaxVisible()
    {
        var tb = GetTextBounds();
        _maxVisible = (int)Math.Floor(tb.Height / _lineHeight);
    }

    private Rectangle GetTextBounds()
    {
        int sbW = _scrollbarVisible ? ScrollbarW : 0;
        return new Rectangle(_bounds.X + Pad, _bounds.Y + Pad,
            _bounds.Width - Pad * 2 - sbW, _bounds.Height - Pad * 2);
    }

    private void InitScrollbar()
    {
        _scrollbar = new Slider(new Rectangle(0, 0, ScrollbarW, 100),
            minValue: 0f, maxValue: 1f, initialValue: 0f, step: 0f, isHorizontal: false,
            trackColor: Color.DarkGray, fillColor: Color.Transparent,
            handleColor: Color.Gray, handleHoverColor: Color.LightGray,
            handlePressedColor: Color.DimGray,
            trackHeight: ScrollbarW - 4, handleSize: ScrollbarW - 2, handleBorderSize: 1);
        _scrollbar.OnValueChanged += _scrollbarHandler;
        _scrollbar.LocalOrderOffset = 0.05f;
    }

    private void RefreshScrollbar()
    {
        if (_syncingScrollbar) return;
        _syncingScrollbar = true;
        try
        {
            bool needs = _wrappedLines.Count > _maxVisible;
            if (needs != _scrollbarVisible)
            {
                _scrollbarVisible = needs;
                CalcMaxVisible();
                needs = _wrappedLines.Count > _maxVisible;
                _scrollbarVisible = needs;
                if (_scrollbarVisible) RebuildWrappedLines();
            }

            if (_scrollbarVisible)
            {
                _scrollbar.SetBounds(new Rectangle(
                    _bounds.Right - ScrollbarW, _bounds.Y, ScrollbarW, _bounds.Height));
                _scrollbar.SetVisibility(true);
                _scrollbar.Order = Order + 0.1f;
                SyncScrollbarPosition();
            }
        }
        finally { _syncingScrollbar = false; }
    }

    private void SyncScrollbarPosition()
    {
        if (!_scrollbarVisible) return;
        int max = Math.Max(0, _wrappedLines.Count - _maxVisible);
        _scrollbar.OnValueChanged -= _scrollbarHandler;
        _scrollbar.Value = max > 0 ? 1f - (float)_scrollOffset / max : 1f;
        _scrollbar.OnValueChanged += _scrollbarHandler;
    }

    private void RebuildHitAreas()
    {
        _hitAreas.Clear();
        var tb = GetTextBounds();

        for (int i = 0; i < _wrappedLines.Count; i++)
        {
            int di = i - _scrollOffset;
            if (di < 0 || di >= _maxVisible) continue;
            string line = _wrappedLines[i];
            if (string.IsNullOrEmpty(line)) continue;

            float lineY = tb.Y + di * _lineHeight;
            foreach (var link in _linkDefs)
            {
                int from = 0;
                while (true)
                {
                    int pos = line.IndexOf(link.MatchText, from, StringComparison.OrdinalIgnoreCase);
                    if (pos < 0) break;
                    float x = _font.MeasureString(line.Substring(0, pos)).X;
                    float w = _font.MeasureString(link.MatchText).X;
                    _hitAreas.Add((new Rectangle((int)(tb.X + x), (int)lineY, (int)w, (int)_lineHeight), link.OnClick, link.Color));
                    from = pos + link.MatchText.Length;
                }
            }
        }
        _hitsDirty = false;
    }

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

        if (_bounds.Contains(mp) && IsParentWindowFocused())
        {
            int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
            if (wheel != 0)
            {
                int max = Math.Max(0, _wrappedLines.Count - _maxVisible);
                _scrollOffset = Math.Max(0, Math.Min(_scrollOffset + (wheel > 0 ? -3 : 3), max));
                _hitsDirty = true;
                SyncScrollbarPosition();
            }
        }

        if (_scrollbarVisible && IsParentWindowFocused()) _scrollbar.Update(deltaTime);

        if (_hitsDirty) RebuildHitAreas();

        _hoveredHitIndex = -1;
        for (int i = 0; i < _hitAreas.Count; i++)
        {
            if (_hitAreas[i].Area.Contains(mp)) { _hoveredHitIndex = i; break; }
        }

        bool clicked = mouse.LeftButton == ButtonState.Pressed &&
                       _prevMouse.LeftButton == ButtonState.Released;
        if (clicked && _hoveredHitIndex >= 0)
        {
            _hitAreas[_hoveredHitIndex].OnClick?.Invoke();
        }

        _prevMouse = mouse;
        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!IsVisible()) return;

        spriteBatch.Draw(_pixel, _bounds, null, _bgColor, 0, Vector2.Zero, SpriteEffects.None, GetActualOrder());

        var tb = GetTextBounds();

        spriteBatch.End();
        var rasterizer = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        var origScissor = Core.GraphicsDevice.ScissorRectangle;
        Core.GraphicsDevice.ScissorRectangle = tb;

        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, DepthStencilState.None, rasterizer);

        for (int i = 0; i < _wrappedLines.Count; i++)
        {
            int di = i - _scrollOffset;
            if (di < 0) continue;
            if (di >= _maxVisible) break;

            string line = _wrappedLines[i];
            if (!string.IsNullOrEmpty(line))
                DrawLine(spriteBatch, line, tb.X, tb.Y + di * _lineHeight, i);
        }

        spriteBatch.End();
        Core.GraphicsDevice.ScissorRectangle = origScissor;

        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

        if (_scrollbarVisible) _scrollbar.Draw(spriteBatch);

        base.Draw(spriteBatch);
    }

    private void DrawLine(SpriteBatch sb, string line, float startX, float y, int _)
    {
        if (_linkDefs.Count == 0 && _highlightDefs.Count == 0)
        {
            sb.DrawString(_font, line, new Vector2(startX, y), _fgColor);
            return;
        }

        // Build spans, brightening any span whose hit area is hovered
        var spans = new List<(int Start, int End, Color Color, bool Underline)>();
        foreach (var hl in _highlightDefs)
        {
            int from = 0;
            while (true)
            {
                int pos = line.IndexOf(hl.MatchText, from, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) break;
                spans.Add((pos, pos + hl.MatchText.Length, hl.Color, false));
                from = pos + hl.MatchText.Length;
            }
        }
        foreach (var link in _linkDefs)
        {
            int from = 0;
            while (true)
            {
                int pos = line.IndexOf(link.MatchText, from, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) break;
                float spanX = _font.MeasureString(line.Substring(0, pos)).X;
                float w = _font.MeasureString(link.MatchText).X;
                var spanRect = new Rectangle((int)(startX + spanX), (int)y, (int)w, (int)_lineHeight);
                bool hovered = _hoveredHitIndex >= 0 && _hitAreas[_hoveredHitIndex].Area == spanRect;
                var color = hovered ? Color.Lerp(link.Color, Color.White, 0.4f) : link.Color;
                spans.Add((pos, pos + link.MatchText.Length, color, true));
                from = pos + link.MatchText.Length;
            }
        }
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));

        float x = startX;
        int cur = 0;
        foreach (var (start, end, color, underline) in spans)
        {
            if (end <= cur) continue;
            int s = Math.Max(start, cur);
            if (s > cur)
            {
                string plain = line.Substring(cur, s - cur);
                sb.DrawString(_font, plain, new Vector2(x, y), _fgColor);
                x += _font.MeasureString(plain).X;
            }
            string linked = line.Substring(s, end - s);
            sb.DrawString(_font, linked, new Vector2(x, y), color);
            float lw = _font.MeasureString(linked).X;
            if (underline)
                sb.Draw(_pixel, new Rectangle((int)x, (int)(y + _lineHeight - 3), (int)lw, 1), null, color, 0, Vector2.Zero, SpriteEffects.None, 0f);
            x += lw;
            cur = end;
        }
        if (cur < line.Length)
        {
            sb.DrawString(_font, line.Substring(cur), new Vector2(x, y), _fgColor);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        RebuildWrappedLines();
    }

    public void Dispose()
    {
        _pixel?.Dispose();
        _scrollbar?.Dispose();
    }
}
