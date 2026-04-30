using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

public class EmailListItemUI : UIElement
{
    private Rectangle _bounds;
    private readonly Email _email;
    private readonly Action<Email> _onClick;
    private bool _lastMousePressed = true;
    private bool _isHovered;
    private readonly Texture2D _pixel;

    public EmailListItemUI(Rectangle bounds, Email email, Action<Email> onClick)
    {
        _bounds = bounds;
        _email = email;
        _onClick = onClick;
        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public override void Update(float deltaTime)
    {
        var mouse = Mouse.GetState();
        bool mouseDown = mouse.LeftButton == ButtonState.Pressed;
        _isHovered = IsFocused && GetBoundingBox().Contains(Core.GetTransformedMousePoint());

        if (_isHovered && mouseDown && !_lastMousePressed)
            _onClick?.Invoke(_email);

        _lastMousePressed = mouseDown;
        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var order = GetActualOrder();

        var bg = _isHovered ? ColorPalette.LightGreen
               : _email.IsRead ? ColorPalette.ActualWhite
               : ColorPalette.White;
        spriteBatch.Draw(_pixel, _bounds, null, bg, 0, Vector2.Zero, SpriteEffects.None, order);

        // Unread dot
        if (!_email.IsRead)
        {
            var dot = new Rectangle(_bounds.X + 8, _bounds.Y + _bounds.Height / 2 - 5, 10, 10);
            spriteBatch.Draw(_pixel, dot, null, ColorPalette.DarkGreen, 0, Vector2.Zero, SpriteEffects.None, order + 0.001f);
        }

        var font = Core.DefaultFont;
        var subject = _email.Subject ?? "(no subject)";
        var subjectColor = _email.IsRead ? ColorPalette.DarkCream : ColorPalette.Black;
        spriteBatch.DrawString(font, subject,
            new Vector2(_bounds.X + 26, _bounds.Y + 8),
            subjectColor, 0, Vector2.Zero, 1f, SpriteEffects.None, order + 0.001f);

        var meta = $"{_email.From}   {_email.Date}";
        spriteBatch.DrawString(font, meta,
            new Vector2(_bounds.X + 26, _bounds.Y + 34),
            ColorPalette.DarkCream, 0, Vector2.Zero, 0.85f, SpriteEffects.None, order + 0.001f);

        // Bottom divider
        spriteBatch.Draw(_pixel,
            new Rectangle(_bounds.X, _bounds.Bottom - 1, _bounds.Width, 1),
            null, ColorPalette.LightGreen, 0, Vector2.Zero, SpriteEffects.None, order + 0.001f);

        base.Draw(spriteBatch);
    }

    public override void SetBounds(Rectangle bounds) { base.SetBounds(bounds); _bounds = bounds; }
    public override Rectangle GetBoundingBox() => _bounds;
}
