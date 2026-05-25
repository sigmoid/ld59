using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class SolutionSuccessOverlay : UIPanel
{
    private readonly Rectangle _bounds;
    private readonly string _message;
    private readonly Action _onComplete;
    private readonly Texture2D _pixel;
    private readonly SpriteFont _font;

    private float _timer = 0f;
    private bool _done = false;

    private const float WhiteFadeInDuration = 0.4f;
    private const float TextFadeInDuration  = 2.5f;
    private const float HoldDuration        = 1.0f;
    private const float TotalDuration = WhiteFadeInDuration + TextFadeInDuration + HoldDuration;

    public SolutionSuccessOverlay(Rectangle bounds, string message, Action onComplete)
    {
        _bounds = bounds;
        _message = message;
        _onComplete = onComplete;
        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _font = Core.Content.Load<SpriteFont>("fonts/Victory");
    }

    public override void Update(float deltaTime)
    {
        if (_done)
        {
            base.Update(deltaTime);
            return;
        }

        _timer += deltaTime;
        if (_timer >= TotalDuration)
        {
            _done = true;
            _onComplete?.Invoke();
        }

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        float whiteAlpha = MathHelper.Clamp(_timer / WhiteFadeInDuration, 0f, 1f);
        float depth = GetActualOrder() + 0.01f;

        spriteBatch.Draw(_pixel, _bounds, null, Color.White * whiteAlpha, 0, Vector2.Zero, SpriteEffects.None, depth);

        float textAlpha = MathHelper.Clamp((_timer - WhiteFadeInDuration) / TextFadeInDuration, 0f, 1f);
        if (textAlpha <= 0f) return;

        var textSize = _font.MeasureString(_message);
        var textPos = new Vector2(
            _bounds.Center.X - textSize.X / 2f,
            _bounds.Center.Y - textSize.Y / 2f);

        spriteBatch.DrawString(_font, _message, textPos, Color.Lerp(Color.White, Color.Black, textAlpha),
            0, Vector2.Zero, 1f, SpriteEffects.None, depth + 0.001f);
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
