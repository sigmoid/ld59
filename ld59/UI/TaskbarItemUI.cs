using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

public class TaskbarItemUI : UIElement, IHoverableUIElement
{
    private bool _isHovered;
    private Rectangle _bounds;
    private Texture2D _icon;
    private string _appName;
    private Texture2D _pixel;
    private bool _lastMouseState = true;


    public TaskbarItemUI(Rectangle bounds, Texture2D icon, string appName)
    {
        _bounds = bounds;
        _icon = icon;
        _appName = appName;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void SetHoverState(bool isHovered) => _isHovered = isHovered;

    public override void Update(float deltaTime)
    {
        var mouseState = Mouse.GetState();
        _isHovered = GetBoundingBox().Contains(Core.GetTransformedMousePoint());

        bool mouseDown = mouseState.LeftButton == ButtonState.Pressed;
        if (_isHovered && mouseDown && !_lastMouseState)
            TaskbarRegistry.BringToFront(_appName);
        _lastMouseState = mouseDown;

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var bgColor = _isHovered ? ColorPalette.LightGreen : ColorPalette.LightGreen * 0.5f;
        spriteBatch.Draw(_pixel, _bounds, bgColor);

        int iconSize = _bounds.Height - 10;
        if (_icon != null)
            spriteBatch.Draw(_icon, new Rectangle(_bounds.X + 5, _bounds.Y + 5, iconSize, iconSize), Color.White);

        base.Draw(spriteBatch);
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
