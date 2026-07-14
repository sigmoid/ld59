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

    // Increments on every player-initiated move (not on programmatic ApplyMove), so callers can tell
    // whether the player has touched the board since a cached solution was computed.
    public int MoveVersion { get; private set; }

    // Increments on ANY board change — player moves and programmatic ApplyMove alike. The UI watches
    // this to re-evaluate winnability after each move, independent of MoveVersion's cache semantics.
    public int StateVersion { get; private set; }

    public SolitaireEngine(SolitaireGameMode mode, float contentWidth)
    {
        _mode = mode;
        _mode.Initialize(contentWidth);
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
            if (stack.Cards.Count == 0 && (stack.IsCompleted || stack.ShowEmptyPlaceholder))
            {
                var emptyPos = stack.Position + contentOffset;
                var slotRect = new Rectangle((int)emptyPos.X, (int)emptyPos.Y, CardWidth, CardHeight);
                if (stack.IsCompleted)
                    _renderer.RenderCardBack(slotRect, spriteBatch, order);
                else
                    _renderer.RenderEmptySlot(slotRect, spriteBatch, order);
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
            if (stack.IsCompleted) continue;   // a removed stack is inert

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
            MoveVersion++;            // a player-made move
            StateVersion++;
            ResolveCompletedStacks();
            return;
        }

        _heldFromStack.Cards.InsertRange(_heldFromIndex, _heldCards);
        _heldCards = null;
    }

    // Applies a move programmatically (e.g. from the solver): moves cards from 'from' starting at
    // fromIndex onto 'to', then resolves any newly completed stacks. No-op if the indices are stale.
    public void ApplyMove(SolitaireStack from, int fromIndex, SolitaireStack to)
    {
        if (from == null || to == null || fromIndex < 0 || fromIndex >= from.Cards.Count) return;

        int count  = from.Cards.Count - fromIndex;
        var moving = from.Cards.GetRange(fromIndex, count);
        from.Cards.RemoveRange(fromIndex, count);
        to.Cards.AddRange(moving);
        StateVersion++;
        ResolveCompletedStacks();
    }

    // Removes any stack the mode considers complete, leaving an inert (card-back) slot behind, then
    // notifies the mode of each completion. The notify pass runs after the scan so the mode may add or
    // remove stacks (e.g. award a free cell) without mutating the list we're iterating.
    private void ResolveCompletedStacks()
    {
        List<SolitaireStack> completed = null;
        foreach (var stack in _mode.Stacks)
        {
            if (!stack.IsCompleted && stack.Cards.Count > 0 && _mode.IsStackComplete(stack))
            {
                stack.Cards.Clear();
                stack.IsCompleted = true;
                (completed ??= new()).Add(stack);
            }
        }

        if (completed != null)
            foreach (var stack in completed)
                _mode.OnStackCompleted(stack);
    }
}
