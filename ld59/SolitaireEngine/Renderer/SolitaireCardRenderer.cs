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
        DrawBackground(bounds, spriteBatch, order);
        DrawCorners(card.CardData, bounds, spriteBatch, font, order + 0.001f);
    }

    public void RenderEmptySlot(Rectangle bounds, SpriteBatch spriteBatch, float order)
    {
        var texture = Core.Content.Load<Texture2D>("images/Solitaire/Card-Background");
        spriteBatch.Draw(texture, bounds, null, new Color(20, 60, 20), 0f, Vector2.Zero, SpriteEffects.None, order);
    }

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

    private void DrawBackground(Rectangle bounds, SpriteBatch spriteBatch, float order)
    {
        var texture = Core.Content.Load<Texture2D>("images/Solitaire/Card-Background");
        spriteBatch.Draw(texture, bounds, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, order);
    }

    private void DrawCardBack(Rectangle bounds, SpriteBatch spriteBatch, float order)
    {
        var texture = Core.Content.Load<Texture2D>("images/Solitaire/Card-Back");
        spriteBatch.Draw(texture, bounds, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, order);
    }
}
