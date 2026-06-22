using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

public class SolitaireCardRenderer
{
    public void RenderCard(SolitaireCardInstance card, Rectangle bounds, SpriteBatch spriteBatch, SpriteFont font, float order)
    {
        if (!card.IsFaceUp)
        {
            DrawCardBack(bounds, spriteBatch, order);
            return;
        }
        if (card.CardData.Symbol != null)
        {
            bool dark = card.CardData.SymbolSuit == SymbolSuit.Dark;
            DrawBackground(bounds, spriteBatch, order, dark ? Color.Black : Color.White);
            DrawSymbolCorners(card.CardData.Symbol, bounds, spriteBatch, font, order + 0.001f, dark ? Color.White : Color.Black);
        }
        else
        {
            DrawBackground(bounds, spriteBatch, order);
            DrawCorners(card.CardData, bounds, spriteBatch, font, order + 0.001f);
        }
    }

    public void RenderEmptySlot(Rectangle bounds, SpriteBatch spriteBatch, float order)
    {
        var texture = Core.Content.Load<Texture2D>("images/Solitaire/Card-Background");
        spriteBatch.Draw(texture, bounds, null, new Color(20, 60, 20), 0f, Vector2.Zero, SpriteEffects.None, order);
    }

    public void RenderCardBack(Rectangle bounds, SpriteBatch spriteBatch, float order)
        => DrawCardBack(bounds, spriteBatch, order);

    private void DrawCorners(SolitaireCardData card, Rectangle bounds, SpriteBatch spriteBatch, SpriteFont font, float order)
    {
        var padding = 10;
        var topLeft     = new Vector2(bounds.X + padding, bounds.Y + padding);
        var bottomRight = new Vector2(bounds.X + bounds.Width - padding, bounds.Y + bounds.Height - padding);

        var character = card.Rank.Character();
        var suitTexture = Core.Content.Load<Texture2D>(card.Suit.TexturePath());
        var rankSize = font.MeasureString(character);
        var iconSize = (int)rankSize.Y;
        var color    = card.Suit.SuitColor();

        // Top-left: rank then suit to its right
        spriteBatch.DrawString(font, character, topLeft, color, 0f, Vector2.Zero, 1f, SpriteEffects.None, order);
        spriteBatch.Draw(suitTexture, new Rectangle((int)topLeft.X + (int)rankSize.X, (int)topLeft.Y, iconSize, iconSize), null, color, 0f, Vector2.Zero, SpriteEffects.None, order);

        // Bottom-right: mirrored — suit to the left, rank to its right
        var totalWidth = rankSize.X + iconSize;
        spriteBatch.DrawString(font, character, bottomRight - new Vector2(rankSize.X, rankSize.Y), color, 0f, Vector2.Zero, 1f, SpriteEffects.None, order);
        spriteBatch.Draw(suitTexture, new Rectangle((int)(bottomRight.X - totalWidth), (int)(bottomRight.Y - iconSize), iconSize, iconSize), null, color, 0f, Vector2.Zero, SpriteEffects.None, order);
    }

    private void DrawSymbolCorners(Symbol symbol, Rectangle bounds, SpriteBatch spriteBatch, SpriteFont font, float order, Color color)
    {
        const int padding  = 10;
        const int iconSize = 30;

        // Textures are white on transparent, so the tint color inks them (black or white).
        var assetPath = System.IO.Path.ChangeExtension(symbol.TexturePath, null);
        var texture   = Core.Content.Load<Texture2D>(assetPath);

        // Temporary: tier number drawn next to the symbol for readability. Remove later.
        var tier     = symbol.Tier.ToString();
        var tierSize = font.MeasureString(tier);

        var topLeft = new Rectangle(bounds.X + padding, bounds.Y + padding, iconSize, iconSize);
        spriteBatch.Draw(texture, topLeft, null, color, 0f, Vector2.Zero, SpriteEffects.None, order);
        spriteBatch.DrawString(font, tier, new Vector2(topLeft.Right + 2, topLeft.Y), color, 0f, Vector2.Zero, 1f, SpriteEffects.None, order);

        var bottomRight = new Rectangle(
            bounds.X + bounds.Width  - padding - iconSize,
            bounds.Y + bounds.Height - padding - iconSize,
            iconSize, iconSize);
        spriteBatch.Draw(texture, bottomRight, null, color, 0f, Vector2.Zero, SpriteEffects.None, order);
        spriteBatch.DrawString(font, tier, new Vector2(bottomRight.X - 2 - tierSize.X, bottomRight.Bottom - tierSize.Y), color, 0f, Vector2.Zero, 1f, SpriteEffects.None, order);
    }

    private void DrawBackground(Rectangle bounds, SpriteBatch spriteBatch, float order, Color? tint = null)
    {
        var texture = Core.Content.Load<Texture2D>("images/Solitaire/Card-Background");
        spriteBatch.Draw(texture, bounds, null, tint ?? Color.White, 0f, Vector2.Zero, SpriteEffects.None, order);
    }

    private void DrawCardBack(Rectangle bounds, SpriteBatch spriteBatch, float order)
    {
        var texture = Core.Content.Load<Texture2D>("images/Solitaire/Card-Back");
        spriteBatch.Draw(texture, bounds, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, order);
    }
}
