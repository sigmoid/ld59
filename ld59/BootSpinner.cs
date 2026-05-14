using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using System;

public class BootSpinner : UIPanel
{
    private readonly Rectangle _bounds;
    private readonly Action _onComplete;
    private Texture2D _pixel;

    private const float Duration = 2.2f;
    private static readonly float RotationSpeed = MathF.PI * 1.5f; // radians/sec
    private const float ArcFraction = 0.72f; // portion of circle that's visible
    private const int NumSegments = 80;
    private const float Radius = 55f;
    private const int DotSize = 6;

    private float _timer = 0f;
    private float _headAngle = -MathF.PI / 2f; // start at 12 o'clock

    public BootSpinner(Rectangle bounds, Action onComplete)
    {
        _bounds = bounds;
        _onComplete = onComplete;
        AddChild(new Canvas(bounds, ColorPalette.Black));

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public override void Update(float deltaTime)
    {
        _timer += deltaTime;
        _headAngle += RotationSpeed * deltaTime;

        if (_timer >= Duration)
            _onComplete?.Invoke();

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        float layerDepth = GetActualOrder() + 0.01f;
        var center = new Vector2(_bounds.Width / 2f, _bounds.Height / 2f);
        float arcRadians = ArcFraction * MathHelper.TwoPi;

        for (int i = 0; i < NumSegments; i++)
        {
            float angle = (i / (float)NumSegments) * MathHelper.TwoPi;

            // Angular distance trailing behind the head
            float diff = (_headAngle - angle) % MathHelper.TwoPi;
            if (diff < 0) diff += MathHelper.TwoPi;

            if (diff > arcRadians) continue;

            // t=1 at head (bright), t=0 at tail (dark)
            float t = 1f - (diff / arcRadians);
            var color = ColorPalette.LightGreen * t;

            float px = center.X + MathF.Cos(angle) * Radius;
            float py = center.Y + MathF.Sin(angle) * Radius;

            spriteBatch.Draw(_pixel,
                new Rectangle((int)(px - DotSize / 2f), (int)(py - DotSize / 2f), DotSize, DotSize),
                null, color, 0, Vector2.Zero, SpriteEffects.None, layerDepth);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
