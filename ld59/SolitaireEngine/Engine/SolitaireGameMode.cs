using System.Collections.Generic;

public abstract class SolitaireGameMode
{
    protected readonly List<SolitaireStack> _allStacks = new();

    public IReadOnlyList<SolitaireStack> Stacks => _allStacks;

    public abstract string Name { get; }

    // Called once per game start to deal and populate stacks
    public abstract void Initialize();

    // The stack that receives stock-click actions; null if the mode has no stock
    public virtual SolitaireStack StockStack => null;

    // Called when the player clicks the stock
    public virtual void OnStockClicked() { }

    public abstract bool IsWon { get; }
}
