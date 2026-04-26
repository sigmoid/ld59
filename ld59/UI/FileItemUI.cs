using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

public class FileItemUI : UIElement, IHoverableUIElement
{
    public Action OnClick;

    private Rectangle _bounds;
    private string _name;
    private Texture2D _icon;
    private Texture2D _pixel;
    private int _iconSize;

    private bool _lastLefClick = true;

    private bool _isHovered = false;
    private GameFile _gameFile;

    public FileItemUI(Rectangle bounds, string name, Texture2D icon, Action onClick, GameFile file)
    {
        _bounds = bounds;
        _name = name;
        _icon = icon;

        _iconSize = bounds.Height - 10;

        _gameFile = file;

        this.OnClick = onClick;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        base.SetBounds(bounds);
    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }

    public override void Update(float dt)
    {
        var mouseState = Mouse.GetState();

        _isHovered = GetBoundingBox().Contains(Core.GetTransformedMousePoint()) && IsFocused;

        if (mouseState.LeftButton == ButtonState.Pressed && _isHovered && !_lastLefClick)
        {
            OnClick?.Invoke();
        }

        _lastLefClick = mouseState.LeftButton == ButtonState.Pressed;

        base.Update(dt);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var bgColor = _isHovered ? ColorPalette.DarkGreen : ColorPalette.LightGreen;
        spriteBatch.Draw(_pixel, _bounds, bgColor);

        Rectangle iconBounds = new Rectangle(_bounds.X + 5, _bounds.Y + 5, _iconSize, _iconSize);
        if (_icon != null)
        {
            spriteBatch.Draw(_icon, iconBounds, Color.White);
        }

        var textColor = _isHovered ? ColorPalette.ActualWhite : ColorPalette.Black;
        var textBounds = new Rectangle(iconBounds.Right + 10, _bounds.Y, _bounds.Width - iconBounds.Width - 20, _bounds.Height);
        spriteBatch.DrawString(Core.DefaultFont, _name, new Vector2(textBounds.X, textBounds.Y + (textBounds.Height / 2) - (Core.DefaultFont.LineSpacing / 2)), textColor);

        if(_gameFile?.IsNewDiscovery == true)
        {
            spriteBatch.Draw(_pixel, new Rectangle(_bounds.Right - 60, _bounds.Y + 5, 55, 30), ColorPalette.Green);
            spriteBatch.DrawString(Core.DefaultFont, "NEW", new Vector2(_bounds.Right - 55, _bounds.Y + 3), ColorPalette.ActualWhite);
        }


        base.Draw(spriteBatch);
    }

    public void SetHoverState(bool isHovered)
    {
        _isHovered = isHovered;
    }
}