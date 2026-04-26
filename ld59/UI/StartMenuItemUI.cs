using System;
using System.Threading.Tasks.Dataflow;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class StartMenuItemUI : UIElement, IHoverableUIElement
{
    private bool _isHovered;

    private Rectangle _bounds;

    private Texture2D _icon;
    private string _text;
    private Texture2D _pixel;
    private Action _onClick;
    private bool _lastMouseState = true;

    public StartMenuItemUI(Rectangle bounds, Texture2D icon, string text, Action onClick)
    {
        _bounds = bounds;
        _icon = icon;
        _text = text;
        _onClick = onClick;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void SetHoverState(bool isHovered)
    {
        _isHovered = isHovered;
    }

    public override void Update(float deltaTime)
    {
        var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
        _isHovered = GetBoundingBox().Contains(Core.GetTransformedMousePoint());

        if (_isHovered && mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed && !_lastMouseState)
        {
            _onClick?.Invoke();
        }
        _lastMouseState = mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (_isHovered)
        {
            spriteBatch.Draw(_pixel, _bounds,ColorPalette.LightGreen);
        }
        else
        {
            spriteBatch.Draw(_pixel, _bounds, ColorPalette.LightGreen * 0.5f);
        }
        if (_icon != null)
        {
            spriteBatch.Draw(_icon, new Rectangle(_bounds.X + 5, _bounds.Y + 5, _bounds.Height - 10, _bounds.Height - 10), Color.White);
        }
        if (!string.IsNullOrEmpty(_text))
        {
            var font = Core.DefaultFont;
            var textPosition = new Vector2(_bounds.X + _bounds.Height + 25, _bounds.Y + (_bounds.Height / 2) - (font.MeasureString(_text).Y / 2));
            spriteBatch.DrawString(font, _text, textPosition, ColorPalette.Black);
        }
        base.Draw(spriteBatch);
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }


}