using System.Collections.Generic;

public abstract class SolitaireGameMode
{
    protected readonly List<SolitaireStack> _allStacks = new();

    public IReadOnlyList<SolitaireStack> Stacks => _allStacks;

    public abstract string Name { get; }

    // Called once per game start to deal and populate stacks.
    // contentWidth is the pixel width available inside the window, so modes can lay out responsively.
    public abstract void Initialize(float contentWidth);

    // The stack that receives stock-click actions; null if the mode has no stock
    public virtual SolitaireStack StockStack => null;

    // Called when the player clicks the stock
    public virtual void OnStockClicked() { }

    // True when a stack forms a finished group that should be removed from play. The engine clears
    // such stacks after each move and marks the slot completed. Default: never.
    public virtual bool IsStackComplete(SolitaireStack stack) => false;

    public abstract bool IsWon { get; }
}
