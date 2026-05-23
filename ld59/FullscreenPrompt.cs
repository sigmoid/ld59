using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

public class FullscreenPrompt : UIPanel
{
    private readonly Rectangle _bounds;
    private readonly Action _onComplete;
    private SpriteFont _font;

    private static readonly string[] _promptLines =
    {
        "  Fullscreen? Y/N",
    };

    private int _revealedLines = 0;
    private float _lineTimer = 0f;
    private const float LineInterval = 0.08f;

    private float _totalTime = 0f;
    private float _brightness = 0f;
    private const float IntroDelay = 0.3f;
    private const float FadeDuration = 1.2f;

    private bool _ready = false;
    private float _cursorBlinkTimer = 0f;
    private bool _cursorVisible = true;

    private Rectangle _yBounds;
    private Rectangle _nBounds;
    private KeyboardState _prevKeys;
    private bool _lastMouseDown = false;
    private bool _chosen = false;

    public FullscreenPrompt(Rectangle bounds, Action onComplete)
    {
        _bounds = bounds;
        _onComplete = onComplete;
        AddChild(new Canvas(bounds, ColorPalette.Black));
        _font = Core.Content.Load<SpriteFont>("fonts/BIOS");
        _prevKeys = Keyboard.GetState();
    }

    public override void Update(float deltaTime)
    {
        if (_chosen)
        {
            base.Update(deltaTime);
            return;
        }

        _totalTime += deltaTime;
        float t = MathHelper.Clamp((_totalTime - IntroDelay) / FadeDuration, 0f, 1f);
        _brightness = t;

        if (_totalTime >= IntroDelay)
        {
            _lineTimer += deltaTime;
            while (_lineTimer >= LineInterval && _revealedLines < _promptLines.Length)
            {
                _revealedLines++;
                _lineTimer -= LineInterval;
            }
        }

        _ready = _revealedLines >= _promptLines.Length;

        if (_ready)
        {
            _cursorBlinkTimer += deltaTime;
            if (_cursorBlinkTimer >= 0.5f)
            {
                _cursorVisible = !_cursorVisible;
                _cursorBlinkTimer = 0f;
            }

            var keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.Y) && !_prevKeys.IsKeyDown(Keys.Y))
                Choose(true);
            else if (keys.IsKeyDown(Keys.N) && !_prevKeys.IsKeyDown(Keys.N))
                Choose(false);
            _prevKeys = keys;

            var mouse = Mouse.GetState();
            bool mouseDown = mouse.LeftButton == ButtonState.Pressed;
            if (mouseDown && !_lastMouseDown)
            {
                var pt = Core.GetTransformedMousePoint();
                if (_yBounds.Contains(pt)) Choose(true);
                else if (_nBounds.Contains(pt)) Choose(false);
            }
            _lastMouseDown = mouseDown;
        }

        base.Update(deltaTime);
    }

    private void Choose(bool fullscreen)
    {
        if (_chosen) return;
        _chosen = true;
        if (fullscreen && !Core.Graphics.IsFullScreen)
            Core.Graphics.ToggleFullScreen();
        _onComplete?.Invoke();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        float lineHeight = _font.MeasureString("A").Y + 2;

        // Block height: prompt lines + gap + [Y] + 1.5 gap + [N] + 2 gap + input line
        float totalBlockHeight = (_promptLines.Length + 6.5f) * lineHeight;

        string yText = "    [Y]  Fullscreen";
        string nText = "    [N]  Windowed";
        float maxWidth = 0f;
        foreach (var line in _promptLines)
            maxWidth = MathF.Max(maxWidth, _font.MeasureString(line).X);
        maxWidth = MathF.Max(maxWidth, _font.MeasureString(yText).X);
        maxWidth = MathF.Max(maxWidth, _font.MeasureString(nText).X);

        float x = _bounds.Center.X - maxWidth / 2f;
        float y = _bounds.Center.Y - totalBlockHeight / 2f;
        float depth = GetActualOrder() + 0.01f;
        Color textColor = ColorPalette.LightGreen * _brightness;

        for (int i = 0; i < _revealedLines && i < _promptLines.Length; i++)
            spriteBatch.DrawString(_font, _promptLines[i], new Vector2(x, y + i * lineHeight),
                textColor, 0, Vector2.Zero, 1f, SpriteEffects.None, depth);

        if (!_ready) return;

        float optY = y + _promptLines.Length * lineHeight + lineHeight;

        var yPos = new Vector2(x, optY);
        var nPos = new Vector2(x, optY + lineHeight * 1.5f);

        var ySize = _font.MeasureString(yText);
        var nSize = _font.MeasureString(nText);
        _yBounds = new Rectangle((int)yPos.X, (int)yPos.Y, (int)ySize.X, (int)ySize.Y);
        _nBounds = new Rectangle((int)nPos.X, (int)nPos.Y, (int)nSize.X, (int)nSize.Y);

        var pt = Core.GetTransformedMousePoint();
        Color yColor = _yBounds.Contains(pt) ? ColorPalette.ActualWhite * _brightness : textColor;
        Color nColor = _nBounds.Contains(pt) ? ColorPalette.ActualWhite * _brightness : textColor;

        spriteBatch.DrawString(_font, yText, yPos, yColor, 0, Vector2.Zero, 1f, SpriteEffects.None, depth);
        spriteBatch.DrawString(_font, nText, nPos, nColor, 0, Vector2.Zero, 1f, SpriteEffects.None, depth);

        float inputY = nPos.Y + lineHeight * 2f;
        spriteBatch.DrawString(_font, "> ", new Vector2(x, inputY), textColor, 0, Vector2.Zero, 1f, SpriteEffects.None, depth);
        if (_cursorVisible)
        {
            float cursorX = x + _font.MeasureString("> ").X;
            spriteBatch.DrawString(_font, "_", new Vector2(cursorX, inputY), textColor, 0, Vector2.Zero, 1f, SpriteEffects.None, depth);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
