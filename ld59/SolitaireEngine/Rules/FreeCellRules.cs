using System;
using System.Collections.Generic;

// Tableau: build down by alternating colour. A descending alternating-colour run may be
// picked up as a unit, but only as many cards as the available free cells / empty columns
// allow (the "supermove" limit), supplied by the owning game mode.
public class FreeCellTableauRules : IStackRules
{
    private readonly Func<int> _maxMovableCards;

    public FreeCellTableauRules(Func<int> maxMovableCards) => _maxMovableCards = maxMovableCards;

    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
    {
        if (!stack[fromIndex].IsFaceUp) return false;
        if (stack.Count - fromIndex > _maxMovableCards()) return false;
        for (int i = fromIndex; i < stack.Count - 1; i++)
            if (!IsValidPair(stack[i], stack[i + 1])) return false;
        return true;
    }

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
    {
        if (stack.Count == 0) return true;          // any card may start an empty column
        return IsValidPair(stack[^1], incoming[0]);
    }

    private static bool IsValidPair(SolitaireCardInstance lower, SolitaireCardInstance upper)
        => IsRed(lower.CardData.Suit) != IsRed(upper.CardData.Suit)
        && (int)lower.CardData.Rank == (int)upper.CardData.Rank + 1;

    private static bool IsRed(SolitaireCardSuit suit)
        => suit == SolitaireCardSuit.Hearts || suit == SolitaireCardSuit.Diamonds;
}

// A free cell holds a single card.
public class FreeCellCellRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
        => fromIndex == stack.Count - 1;

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
        => stack.Count == 0 && incoming.Count == 1;
}
