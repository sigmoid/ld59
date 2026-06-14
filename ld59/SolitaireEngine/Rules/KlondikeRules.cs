using System.Collections.Generic;

public class KlondikeTableauRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
    {
        if (!stack[fromIndex].IsFaceUp) return false;
        for (int i = fromIndex; i < stack.Count - 1; i++)
            if (!IsValidPair(stack[i], stack[i + 1])) return false;
        return true;
    }

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
    {
        var card = incoming[0];
        if (stack.Count == 0)
            return card.CardData.Rank == SolitaireCardRank.King;
        var top = stack[^1];
        return top.IsFaceUp && IsValidPair(top, card);
    }

    private static bool IsValidPair(SolitaireCardInstance lower, SolitaireCardInstance upper)
        => IsRed(lower.CardData.Suit) != IsRed(upper.CardData.Suit)
        && (int)lower.CardData.Rank == (int)upper.CardData.Rank + 1;

    private static bool IsRed(SolitaireCardSuit suit)
        => suit == SolitaireCardSuit.Hearts || suit == SolitaireCardSuit.Diamonds;
}

public class KlondikeFoundationRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
        => fromIndex == stack.Count - 1;

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
    {
        if (incoming.Count != 1) return false;
        var card = incoming[0];
        if (stack.Count == 0)
            return card.CardData.Rank == SolitaireCardRank.Ace;
        var top = stack[^1];
        return card.CardData.Suit == top.CardData.Suit
            && (int)card.CardData.Rank == (int)top.CardData.Rank + 1;
    }
}

public class KlondikeWasteRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
        => fromIndex == stack.Count - 1;

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
        => false;
}

public class KlondikeStockRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex) => false;
    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming) => false;
}
