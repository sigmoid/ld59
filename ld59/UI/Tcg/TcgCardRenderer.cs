using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using ld59.UI.Powergrid;

/// <summary>
/// Draws TCG card faces. Word cards need a row of symbol stamps plus tier/sidedness glyphs,
/// which SolitaireCardRenderer's single-symbol face can't do — but backs and empty slots
/// delegate to it, and faces reuse its background texture and black/white inking so the two
/// card games share one visual language. All sizes derive from the target rectangle: the TCG
/// board draws cards smaller than solitaire's fixed 100x150.
/// </summary>
public class TcgCardRenderer
{
    private readonly SolitaireCardRenderer _solitaire = new();

    /// <summary>dark = white-on-black (opponent's cards); light = black-on-white (player's).</summary>
    public void DrawFace(TcgCard card, Rectangle bounds, bool dark, SpriteBatch sb, SpriteFont font, float order)
    {
        var bg = Core.Content.Load<Texture2D>("images/Solitaire/Card-Background");
        sb.Draw(bg, bounds, null, dark ? Color.Black : Color.White, 0f, Vector2.Zero, SpriteEffects.None, order);
        var ink = dark ? Color.White : Color.Black;

        // Symbol stamps in a centered row across the upper half.
        int n = card.Length;
        const int gap = 3;
        int icon = System.Math.Min(bounds.Width * 2 / 5, (bounds.Width - 10 - (n - 1) * gap) / n);
        int totalW = n * icon + (n - 1) * gap;
        int x = bounds.X + (bounds.Width - totalW) / 2;
        int y = bounds.Y + (int)(bounds.Height * 0.34f) - icon / 2;
        foreach (var sym in card.Symbols)
        {
            var tex = Runes.Texture(sym.Name);
            if (tex != null)
                sb.Draw(tex, new Rectangle(x, y, icon, icon), null, ink, 0f, Vector2.Zero, SpriteEffects.None, order);
            x += icon + gap;
        }

        float scale = MathHelper.Clamp(bounds.Height / 150f, 0.55f, 1f);

        // Tier + sidedness glyph ("4L") top-left, mirrored bottom-right — sidedness has no icon
        // anywhere in the game yet, so a letter keeps it readable at 1-bit.
        string stat = $"{card.Tier}{WordDictionary.SideGlyph(card.Sidedness)}";
        var statSize = font.MeasureString(stat) * scale;
        const int pad = 5;
        sb.DrawString(font, stat, new Vector2(bounds.X + pad, bounds.Y + pad), ink,
            0f, Vector2.Zero, scale, SpriteEffects.None, order);
        sb.DrawString(font, stat, new Vector2(bounds.Right - pad - statSize.X, bounds.Bottom - pad - statSize.Y), ink,
            0f, Vector2.Zero, scale, SpriteEffects.None, order);

        // Word cards carry their name under the stamps (doubles as the description area).
        if (card.Word != null)
        {
            var name = card.Word.Name;
            var raw = font.MeasureString(name);
            float nameScale = System.Math.Min(scale, (bounds.Width - 8) / raw.X);
            sb.DrawString(font, name,
                new Vector2(bounds.X + (bounds.Width - raw.X * nameScale) / 2, bounds.Y + bounds.Height * 0.62f),
                ink, 0f, Vector2.Zero, nameScale, SpriteEffects.None, order);
        }
    }

    public void DrawBack(Rectangle bounds, SpriteBatch sb, float order) => _solitaire.RenderCardBack(bounds, sb, order);

    public void DrawEmptySlot(Rectangle bounds, SpriteBatch sb, float order) => _solitaire.RenderEmptySlot(bounds, sb, order);
}
