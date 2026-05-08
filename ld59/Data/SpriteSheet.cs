using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class SpriteSheet
{
    public Texture2D Texture { get; set; }
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public float Fps { get; set; } = 12f;

    public int TotalFrames => Columns * Rows;
    public int FrameWidth => Texture.Width / Columns;
    public int FrameHeight => Texture.Height / Rows;

    public Rectangle GetFrameRect(int frame)
    {
        int col = frame % Columns;
        int row = frame / Columns;
        return new Rectangle(col * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight);
    }
}
