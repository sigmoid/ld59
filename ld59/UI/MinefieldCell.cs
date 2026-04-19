using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.Graphics;
using Quartz.UI;

public class MinefieldCell : UIElement, IHoverableUIElement
{
    public bool IsRevealed { get; set; } = false;
    public bool HasMine { get; set; } = false;
    public int NeighborCount { get; set; } = 0;

    public Action<(int,int)> OnClick {get;set;}
    private Rectangle _bounds;
    private static Texture2D _mineTexture;
    private static Texture2D _cellTexture;
    private (int,int) _position;
    private bool _isHovered = false;
    private bool _isFocused = false;
    private bool _lastLeftClick = false;

    public MinefieldCell(Rectangle bounds, (int,int) position)
    {
        _cellTexture = Core.Content.Load<Texture2D>("images/minefield_cell");
        _mineTexture = Core.Content.Load<Texture2D>("images/minefield_mine");
        _bounds = bounds;
        _position = position;   
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

    public override void Update(float deltaTime)
    {
        var mouseState = Mouse.GetState();

        _isHovered = GetBoundingBox().Contains(mouseState.Position) && _isFocused;

        if(_isHovered && mouseState.LeftButton == ButtonState.Pressed && !_lastLeftClick)
        {
            OnClick?.Invoke(_position);
            IsRevealed = true;
        }

        _lastLeftClick = mouseState.LeftButton == ButtonState.Pressed;

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {

        if(!IsRevealed)
        {
            var color = (_isHovered ? Color.LightGray : Color.White);
            spriteBatch.Draw(_cellTexture, _bounds, color);
        }
        else if (HasMine)
        {
            spriteBatch.Draw(_mineTexture, _bounds, Color.White);
        }
        else if(NeighborCount > 0)
        {
            spriteBatch.DrawString(Core.DefaultFont, NeighborCount.ToString(), new Vector2(_bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2) - Core.DefaultFont.MeasureString(NeighborCount.ToString()) / 2, Color.White);
        }


        base.Draw(spriteBatch);
    }

    public override void OnFocus()
    {
        _isFocused = true;
        base.OnFocus();
    }

    public override void OnLostFocus()
    {
        _isFocused = false;
        base.OnLostFocus();
    }

    public void SetHoverState(bool isHovered)
    {
        _isHovered = isHovered;
    }
}