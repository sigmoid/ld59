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
    private GameFolder _gameFolder;
    private SpriteFont _smallFont;

    public FileItemUI(Rectangle bounds, string name, Texture2D icon, Action onClick, GameFile file, GameFolder folder = null)
    {
        _bounds = bounds;
        _name = name;
        _icon = icon;

        _iconSize = bounds.Height - 10;

        _gameFile = file;
        _gameFolder = folder;

        this.OnClick = onClick;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _smallFont = Core.Content.Load<SpriteFont>("fonts/Small");
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
            if (_gameFile != null) _gameFile.IsNewDiscovery = false;
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

        if(_gameFile?.IsEncrypted == true)
        {
            spriteBatch.Draw(_pixel, new Rectangle(_bounds.Right - 80, _bounds.Y + 5, 70, 30), Color.DarkRed);
            spriteBatch.DrawString(_smallFont, "LOCKED", new Vector2(_bounds.Right - 70, _bounds.Center.Y - (_smallFont.LineSpacing / 2)), ColorPalette.ActualWhite);
        }

        var isNew = (_gameFile?.IsNewDiscovery == true && _gameFile?.IsEncrypted != true) || _gameFolder?.HasNewItems() == true;
        if (isNew)
        {
            var newBadgeOffset = _gameFile?.IsEncrypted == true ? 145 : 60;
            spriteBatch.Draw(_pixel, new Rectangle(_bounds.Right - newBadgeOffset, _bounds.Y + 5, 40, 30), ColorPalette.Green);
            spriteBatch.DrawString(_smallFont, "NEW", new Vector2(_bounds.Right - newBadgeOffset + 5, _bounds.Center.Y - (_smallFont.LineSpacing / 2)), ColorPalette.ActualWhite);
        }


        base.Draw(spriteBatch);
    }

    public void SetHoverState(bool isHovered)
    {
        _isHovered = isHovered;
    }
}