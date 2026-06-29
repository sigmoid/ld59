using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.Graphics;
using Quartz.UI;
using ld59.UI.Powergrid;

public class MinefieldCell : UIElement, IHoverableUIElement
{
    public bool IsRevealed { get; set; } = false;
    public bool HasMine { get; set; } = false;
    public bool IsFlagged { get; set; } = false;

    private int _neighborCount = 0;
    /// <summary>Number of adjacent mines. Setting it invalidates the cached rune so a cell reused
    /// across a game reset re-picks a rune for its new tier instead of keeping the stale one.</summary>
    public int NeighborCount
    {
        get => _neighborCount;
        set
        {
            if (_neighborCount == value) return;
            _neighborCount = value;
            _runeResolved = false;
        }
    }

    private int _mineLean = 0;
    /// <summary>Which side the adjacent mines sit on, relative to this cell: -1 = only on the left,
    /// +1 = only on the right, 0 = on both sides, or only directly above/below. Chooses the left/right/
    /// centre variant of the rune. Invalidates the cached rune on change (see <see cref="NeighborCount"/>).</summary>
    public int MineLean
    {
        get => _mineLean;
        set
        {
            if (_mineLean == value) return;
            _mineLean = value;
            _runeResolved = false;
        }
    }

    public Action<(int,int)> OnClick {get;set;}
    private Rectangle _bounds;
    private static Texture2D _mineTexture;
    private static Texture2D _cellTexture;
    private static Texture2D _flagTexture;
    private static Texture2D _pixel;
    private (int,int) _position;
    private bool _isHovered = false;
    private bool _isFocused = false;
    private bool _lastLeftClick = true;
    private bool _lastRightClick = true;

    private static readonly System.Random _runeRng = new System.Random();
    /// <summary>The rune shown in place of the neighbour count: a random symbol whose tier equals
    /// <see cref="NeighborCount"/>. Null when no tier matches (count exceeds the tier count) — then the
    /// raw number is shown. Resolved once and cached so it doesn't flicker between frames.</summary>
    private string _runeName;
    /// <summary>When the mines straddle both sides but the tier row has no centre rune, the display
    /// alternates between the two innermost (middle) runes instead of picking one. Null otherwise.</summary>
    private string[] _flashRunes;
    /// <summary>For counts beyond the number of tiers: the count written in bijective base-(tier count),
    /// one representative rune per digit, drawn left-to-right. Null otherwise.</summary>
    private string[] _digitRunes;
    private bool _runeResolved = false;
    /// <summary>Half-period of the two-rune flash, in milliseconds.</summary>
    private const long FlashIntervalMs = 450;

    private void ResolveRune()
    {
        _runeResolved = true;
        _runeName = null;
        _flashRunes = null;
        _digitRunes = null;

        int tierCount = SymbolDictionary.All.Max(s => s.Tier);
        var tierRunes = SymbolDictionary.All.Where(s => s.Tier == NeighborCount)
            .OrderBy(s => s.RowOrder).ToList();

        if (tierRunes.Count == 0)
        {
            // Beyond the tier count: write the number in bijective base-(tierCount) — one rune per digit.
            if (NeighborCount > tierCount)
                _digitRunes = BijectiveDigits(NeighborCount, tierCount)
                    .Select(RepresentativeRune).ToArray();
            return;
        }
        if (tierRunes.Count == 1)                    // tier 1 (Lith): the single rune, no sides
        {
            _runeName = tierRunes[0].Name;
            return;
        }

        if (_mineLean > 0)                           // mines only on the right
        {
            var pool = tierRunes.Where(s => ColoringRules.HorizontalSide(s) > 0).ToList();
            _runeName = (pool.Count > 0 ? pool : tierRunes)[_runeRng.Next(pool.Count > 0 ? pool.Count : tierRunes.Count)].Name;
        }
        else if (_mineLean < 0)                      // mines only on the left
        {
            var pool = tierRunes.Where(s => ColoringRules.HorizontalSide(s) < 0).ToList();
            _runeName = (pool.Count > 0 ? pool : tierRunes)[_runeRng.Next(pool.Count > 0 ? pool.Count : tierRunes.Count)].Name;
        }
        else                                         // mines on both sides (or only above/below)
        {
            var center = tierRunes.Where(s => ColoringRules.HorizontalSide(s) == 0).ToList();
            if (center.Count > 0)
                _runeName = center[0].Name;          // odd row: the single centre rune
            else
            {
                // Even row, no centre rune: flash between the two innermost (middle) runes.
                int n = tierRunes.Count;
                _flashRunes = new[] { tierRunes[n / 2 - 1].Name, tierRunes[n / 2].Name };
            }
        }
    }

    /// <summary>The rune name to draw this frame: the resolved single rune, or — when flashing — one of
    /// the two middle runes chosen by wall-clock time so all flashing cells stay in sync.</summary>
    private string ActiveRune()
        => _flashRunes != null
            ? _flashRunes[(int)(System.Environment.TickCount64 / FlashIntervalMs % 2)]
            : _runeName;

    /// <summary>Digits of <paramref name="n"/> in bijective base-<paramref name="b"/> (digits 1..b, no
    /// zero), most-significant first. Each digit is a tier number, e.g. 8 in base 5 -> [1, 3].</summary>
    private static List<int> BijectiveDigits(int n, int b)
    {
        var digits = new List<int>();
        while (n > 0)
        {
            digits.Insert(0, (n - 1) % b + 1);
            n = (n - 1) / b;
        }
        return digits;
    }

    /// <summary>A representative rune for a tier digit: the first by row order (e.g. tier 2 -> Axe).</summary>
    private static string RepresentativeRune(int tier)
        => SymbolDictionary.All.Where(s => s.Tier == tier).OrderBy(s => s.RowOrder).FirstOrDefault()?.Name;

    public MinefieldCell(Rectangle bounds, (int,int) position)
    {
        _cellTexture = Core.Content.Load<Texture2D>("images/minefield_cell");
        _mineTexture = Core.Content.Load<Texture2D>("images/minefield_mine");
        _flagTexture = Core.Content.Load<Texture2D>("images/flag");
        if (_pixel == null)
        {
            _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        _bounds = bounds;
        _position = position;
    }

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        base.SetBounds(bounds);
    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }

    public override void Update(float deltaTime)
    {
        var mouseState = Mouse.GetState();

        _isHovered = GetBoundingBox().Contains(Core.GetTransformedMousePoint()) && _isFocused;

        if(_isHovered && mouseState.RightButton == ButtonState.Pressed && !_lastRightClick)
        {
            IsFlagged = !IsFlagged;
        }

        if(_isHovered && mouseState.LeftButton == ButtonState.Pressed && !_lastLeftClick)
        {
            OnClick?.Invoke(_position);
            IsRevealed = true;
        }

        _lastLeftClick = mouseState.LeftButton == ButtonState.Pressed;
        _lastRightClick = mouseState.RightButton == ButtonState.Pressed;

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {

        if(!IsRevealed)
        {
            var color = (_isHovered ? Color.LightGray : Color.White);
            spriteBatch.Draw(_cellTexture, _bounds, color);

            if(IsFlagged)
            {
                var flagRect = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height);
                spriteBatch.Draw(_flagTexture, flagRect, Color.White);
            }
        }
        else
        {
            // Keep the grid readable once a cell is explored: outline it in gray.
            DrawBorder(spriteBatch, _bounds, System.Math.Max(1, _bounds.Width / 24), Color.Gray);

            if (HasMine)
            {
                spriteBatch.Draw(_mineTexture, _bounds, Color.White);
            }
            else if(NeighborCount > 0)
            {
                if(!_runeResolved) ResolveRune();

                if(_digitRunes != null)
                {
                    DrawDigitRunes(spriteBatch);
                }
                else
                {
                    var activeRune = ActiveRune();
                    var runeTexture = activeRune != null ? Runes.Texture(activeRune) : null;
                    if(runeTexture != null)
                    {
                        int pad = _bounds.Width / 6;
                        var runeRect = new Rectangle(_bounds.X + pad, _bounds.Y + pad, _bounds.Width - pad * 2, _bounds.Height - pad * 2);
                        spriteBatch.Draw(runeTexture, runeRect, Color.Black);
                    }
                    else
                    {
                        DrawCountNumber(spriteBatch);
                    }
                }
            }
        }


        base.Draw(spriteBatch);
    }

    /// <summary>Draws the base-(tier count) digit runes left-to-right, each squared and centred in its
    /// slot. Falls back to the raw number if a rune texture is missing.</summary>
    private void DrawDigitRunes(SpriteBatch sb)
    {
        int n = _digitRunes.Length;
        int pad = _bounds.Width / 12;
        int slotW = (_bounds.Width - pad * 2) / n;
        int size = System.Math.Min(slotW, _bounds.Height - pad * 2);

        for (int i = 0; i < n; i++)
        {
            var tex = Runes.Texture(_digitRunes[i]);
            if (tex == null) { DrawCountNumber(sb); return; }
            int cx = _bounds.X + pad + slotW * i + slotW / 2;
            int cy = _bounds.Y + _bounds.Height / 2;
            sb.Draw(tex, new Rectangle(cx - size / 2, cy - size / 2, size, size), Color.Black);
        }
    }

    private void DrawCountNumber(SpriteBatch sb)
    {
        var text = NeighborCount.ToString();
        sb.DrawString(Core.DefaultFont, text,
            new Vector2(_bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2) - Core.DefaultFont.MeasureString(text) / 2,
            Color.Black);
    }

    /// <summary>Draws a hollow rectangle outline of the given thickness/colour.</summary>
    private void DrawBorder(SpriteBatch sb, Rectangle r, int thickness, Color color)
    {
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, thickness), color);                       // top
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);      // bottom
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, thickness, r.Height), color);                      // left
        sb.Draw(_pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);      // right
    }

    public override void OnFocus()
    {
        _isFocused = true;
        base.OnFocus();
    }

    public override void OnLostFocus()
    {
        _isFocused = false;
        base.OnLostFocus();
    }

    public void SetHoverState(bool isHovered)
    {
        _isHovered = isHovered;
    }
}