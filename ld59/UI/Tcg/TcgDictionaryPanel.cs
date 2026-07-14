using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

/// <summary>
/// The shared word dictionary (tcg.md's "extra deck"), browsable in a scrolling overlay on the
/// right side of the TCG window. Scrolling is hand-rolled (wheel delta + scissor clip) in the
/// same style as the rest of the board panel rather than going through Quartz's ScrollArea —
/// the rows are draw calls, not UIElements.
/// </summary>
public class TcgDictionaryPanel : UIElement
{
    private const int RowH = 100;
    private const int HeaderH = 42;
    private const int Pad = 10;

    private Rectangle _bounds;
    private readonly TcgCardRenderer _cards = new();
    private readonly SpriteFont _uiFont;
    private readonly Texture2D _white;
    private float _scroll;
    private int _prevWheel;

    public TcgDictionaryPanel(Rectangle bounds)
    {
        _bounds = bounds;
        _uiFont = Core.DefaultFont;
        _white = new Texture2D(Core.GraphicsDevice, 1, 1);
        _white.SetData(new[] { Color.White });
        _prevWheel = Mouse.GetState().ScrollWheelValue;
    }

    public override Rectangle GetBoundingBox() => _bounds;
    public override void SetBounds(Rectangle bounds) => _bounds = bounds;

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        int wheel = Mouse.GetState().ScrollWheelValue;
        int delta = wheel - _prevWheel;
        _prevWheel = wheel;
        if (delta != 0 && _bounds.Contains(Core.GetTransformedMousePoint()))
        {
            float maxScroll = Math.Max(0, WordDictionary.All.Count * RowH + HeaderH + Pad - _bounds.Height);
            _scroll = MathHelper.Clamp(_scroll - delta * 0.4f, 0, maxScroll);
        }
    }

    public override void Draw(SpriteBatch sb)
    {
        sb.End();
        var rasterizer = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        var origScissor = Core.GraphicsDevice.ScissorRectangle;
        Core.GraphicsDevice.ScissorRectangle = _bounds;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer);

        sb.Draw(_white, _bounds, Color.Black);

        const string title = "DICTIONARY";
        var titleSize = _uiFont.MeasureString(title) * 1.2f;
        sb.DrawString(_uiFont, title,
            new Vector2(_bounds.X + (_bounds.Width - titleSize.X) / 2, _bounds.Y + Pad - _scroll),
            ColorPalette.ActualWhite, 0f, Vector2.Zero, 1.2f, SpriteEffects.None, 0f);

        int thumbW = 58, thumbH = 87;
        for (int i = 0; i < WordDictionary.All.Count; i++)
        {
            var w = WordDictionary.All[i];
            int rowY = (int)(_bounds.Y + HeaderH + i * RowH - _scroll);
            if (rowY + RowH < _bounds.Y || rowY > _bounds.Bottom) continue;

            var thumb = new Rectangle(_bounds.X + Pad, rowY + (RowH - thumbH) / 2, thumbW, thumbH);
            _cards.DrawFace(TcgCard.FromWord(w), thumb, dark: false, sb, _uiFont, 0f);

            int textX = thumb.Right + Pad;
            sb.DrawString(_uiFont, $"{w.Name} \"{w.Meaning}\"", new Vector2(textX, rowY + 16), ColorPalette.ActualWhite);
            var stats = $"Tier {w.Tier} - {WordDictionary.SideGlyph(w.Sidedness)} - {w.Length} symbols";
            sb.DrawString(_uiFont, stats, new Vector2(textX, rowY + 16 + _uiFont.LineSpacing + 4), new Color(190, 210, 190));

            sb.Draw(_white, new Rectangle(_bounds.X + Pad, rowY + RowH - 1, _bounds.Width - 2 * Pad, 1), new Color(70, 90, 70));
        }

        // Panel border, drawn inside the scissor so it survives window clipping.
        const int t = 2;
        sb.Draw(_white, new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, t), ColorPalette.ActualWhite);
        sb.Draw(_white, new Rectangle(_bounds.X, _bounds.Bottom - t, _bounds.Width, t), ColorPalette.ActualWhite);
        sb.Draw(_white, new Rectangle(_bounds.X, _bounds.Y, t, _bounds.Height), ColorPalette.ActualWhite);
        sb.Draw(_white, new Rectangle(_bounds.Right - t, _bounds.Y, t, _bounds.Height), ColorPalette.ActualWhite);

        sb.End();
        Core.GraphicsDevice.ScissorRectangle = origScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
    }
}
