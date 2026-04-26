using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using System;

public class VictoryScreen : UIPanel
{
    private readonly Rectangle _bounds;
    private readonly string[] _lines;
    private readonly Action _onComplete;

    private int _revealedLines = 0;
    private float _lineTimer = 0f;
    private float _postRevealTimer = 0f;
    private bool _done = false;

    private const float LineInterval = 0.095f;
    private const float PostRevealDelay = 3.0f;

    private float _cursorBlinkTimer = 0f;
    private bool _cursorVisible = true;

    private SpriteFont _font;

    public VictoryScreen(Rectangle bounds, Action onComplete = null)
    {
        _bounds = bounds;
        _onComplete = onComplete;

        AddChild(new Canvas(bounds, ColorPalette.Black));

        try
        {
            _lines = System.IO.File.ReadAllLines("Content/victory_text.txt");
        }
        catch
        {
            _lines = new[] { "MISSION COMPLETE" };
        }

        _font = Core.Content.Load<SpriteFont>("fonts/BIOS");
    }

    public override void Update(float deltaTime)
    {
        if (_done)
        {
            base.Update(deltaTime);
            return;
        }

        if (_revealedLines < _lines.Length)
        {
            _lineTimer += deltaTime;
            while (_lineTimer >= LineInterval && _revealedLines < _lines.Length)
            {
                _revealedLines++;
                _lineTimer -= LineInterval;
            }

            _cursorBlinkTimer += deltaTime;
            if (_cursorBlinkTimer >= 0.5f)
            {
                _cursorVisible = !_cursorVisible;
                _cursorBlinkTimer = 0f;
            }
        }
        else
        {
            _cursorVisible = false;
            _postRevealTimer += deltaTime;
            if (_postRevealTimer >= PostRevealDelay)
            {
                _done = true;
                _onComplete?.Invoke();
            }
        }

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        float lineHeight = _font.MeasureString("A").Y + 1;
        float x = 30f;
        float startY = 20f;
        float layerDepth = GetActualOrder() + 0.01f;
        Color textColor = ColorPalette.LightGreen;

        for (int i = 0; i < _revealedLines && i < _lines.Length; i++)
        {
            float y = startY + i * lineHeight;
            if (y + lineHeight > _bounds.Height) break;

            spriteBatch.DrawString(_font, _lines[i], new Vector2(x, y), textColor,
                0, Vector2.Zero, 1.0f, SpriteEffects.None, layerDepth);
        }

        if (_cursorVisible && _revealedLines > 0 && _revealedLines <= _lines.Length)
        {
            float cursorY = startY + (_revealedLines - 1) * lineHeight;
            string lastLine = _lines[_revealedLines - 1];
            float cursorX = x + _font.MeasureString(lastLine).X + 2;
            spriteBatch.DrawString(_font, "_", new Vector2(cursorX, cursorY), textColor,
                0, Vector2.Zero, 1.0f, SpriteEffects.None, layerDepth);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
