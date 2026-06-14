using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using System.Collections.Generic;

public class SolitaireEngine
{
    private readonly SolitaireCardRenderer _renderer = new();
    private readonly SolitaireGameMode _mode;
    private const int CardWidth  = 100;
    private const int CardHeight = 150;

    private List<SolitaireCardInstance> _heldCards;
    private SolitaireStack _heldFromStack;
    private int     _heldFromIndex;
    private Vector2 _heldBasePosition;
    private Vector2 _heldDragOffset;

    private MouseState _previousMouseState;

    public bool IsWon => _mode.IsWon;

    public SolitaireEngine(SolitaireGameMode mode)
    {
        _mode = mode;
        _mode.Initialize();
    }

    public void Update(GameTime gameTime, Vector2 contentOffset)
    {
        var mouseState = Mouse.GetState();
        var rawMouse   = Core.GetTransformedMousePoint();
        var localMouse = new Vector2(rawMouse.X - contentOffset.X, rawMouse.Y - contentOffset.Y);

        bool justPressed  = mouseState.LeftButton == ButtonState.Pressed  && _previousMouseState.LeftButton == ButtonState.Released;
        bool justReleased = mouseState.LeftButton == ButtonState.Released  && _previousMouseState.LeftButton == ButtonState.Pressed;

        if (justPressed)
            TryPickUp(localMouse);

        if (mouseState.LeftButton == ButtonState.Pressed && _heldCards != null)
            _heldBasePosition = localMouse + _heldDragOffset;

        if (justReleased && _heldCards != null)
            TryDrop();

        _previousMouseState = mouseState;
    }

    public void Draw(SpriteBatch spriteBatch, SpriteFont font, float order, Vector2 contentOffset)
    {
        foreach (var stack in _mode.Stacks)
        {
            if (stack.Cards.Count == 0 && stack.ShowEmptyPlaceholder)
            {
                var emptyPos = stack.Position + contentOffset;
                _renderer.RenderEmptySlot(new Rectangle((int)emptyPos.X, (int)emptyPos.Y, CardWidth, CardHeight), spriteBatch, order);
            }

            for (int i = 0; i < stack.Cards.Count; i++)
            {
                var pos    = stack.Position + stack.Layout.GetCardOffset(stack.Cards, i) + contentOffset;
                var bounds = new Rectangle((int)pos.X, (int)pos.Y, CardWidth, CardHeight);
                _renderer.RenderCard(stack.Cards[i], bounds, spriteBatch, font, order);
            }
        }

        if (_heldCards != null)
        {
            for (int i = 0; i < _heldCards.Count; i++)
            {
                var pos    = _heldBasePosition + _heldFromStack.Layout.GetCardOffset(_heldCards, i) + contentOffset;
                var bounds = new Rectangle((int)pos.X, (int)pos.Y, CardWidth, CardHeight);
                _renderer.RenderCard(_heldCards[i], bounds, spriteBatch, font, order);
            }
        }
    }

    private void TryPickUp(Vector2 localMouse)
    {
        if (_mode.StockStack != null)
        {
            var stockRect = new Rectangle((int)_mode.StockStack.Position.X, (int)_mode.StockStack.Position.Y, CardWidth, CardHeight);
            if (stockRect.Contains((int)localMouse.X, (int)localMouse.Y))
            {
                _mode.OnStockClicked();
                return;
            }
        }

        foreach (var stack in _mode.Stacks)
        {
            int total = stack.Cards.Count;
            for (int i = total - 1; i >= 0; i--)
            {
                var pos  = stack.Position + stack.Layout.GetCardOffset(stack.Cards, i);
                var rect = new Rectangle((int)pos.X, (int)pos.Y, CardWidth, CardHeight);
                if (!rect.Contains((int)localMouse.X, (int)localMouse.Y)) continue;

                if (!stack.Rules.CanPickUp(stack.Cards, i)) return;

                _heldCards        = stack.Cards.GetRange(i, total - i);
                stack.Cards.RemoveRange(i, total - i);
                _heldFromStack    = stack;
                _heldFromIndex    = i;
                _heldBasePosition = pos;
                _heldDragOffset   = pos - localMouse;
                return;
            }
        }
    }

    private void TryDrop()
    {
        var heldRect = new Rectangle((int)_heldBasePosition.X, (int)_heldBasePosition.Y, CardWidth, CardHeight);

        foreach (var stack in _mode.Stacks)
        {
            if (stack == _heldFromStack) continue;

            int total     = stack.Cards.Count;
            var targetPos = total > 0
                ? stack.Position + stack.Layout.GetCardOffset(stack.Cards, total - 1)
                : stack.Position;

            var targetRect = new Rectangle((int)targetPos.X, (int)targetPos.Y, CardWidth, CardHeight);
            if (!targetRect.Intersects(heldRect)) continue;

            if (!stack.Rules.CanAccept(stack.Cards, _heldCards)) continue;

            stack.Cards.AddRange(_heldCards);
            if (_heldFromStack.Cards.Count > 0 && !_heldFromStack.Cards[^1].IsFaceUp)
                _heldFromStack.Cards[^1].IsFaceUp = true;
            _heldCards = null;
            return;
        }

        _heldFromStack.Cards.InsertRange(_heldFromIndex, _heldCards);
        _heldCards = null;
    }
}
