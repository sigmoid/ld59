using System.Collections.Generic;

// The single source of truth for symbol stacking, shared by the tableau rules and the solver
// so they can never drift apart. Suits are passed as ints ((int)SymbolSuit).
public static class SymbolStackRule
{
    // The strict relationship that builds a complete run (the win): 'upper' is one tier up from
    // 'lower' and the opposite suit.
    public static bool IsRunStep(int lowerTier, int lowerSuit, int upperTier, int upperSuit)
        => upperTier == lowerTier - 1 && upperSuit != lowerSuit;

    // Whether 'upper' may be placed on a column whose top is 'lower' and whose card directly beneath
    // the top has tier belowTopTier (-1 if the top is the only card). isSingle = moving exactly one card.
    //   - a single card may park on a same-tier top (even onto an existing same-tier pile);
    //   - otherwise it must be a run step, AND the top must not already be part of a same-tier pile
    //     (you can't stack another tier on top of two-or-more cards of the same tier).
    public static bool CanPlaceOn(int lowerTier, int lowerSuit, int belowTopTier, int upperTier, int upperSuit, bool isSingle)
    {
        if (isSingle && upperTier == lowerTier) return true;   // same-tier parking
        if (belowTopTier == lowerTier) return false;           // top is part of a same-tier pile
        return IsRunStep(lowerTier, lowerSuit, upperTier, upperSuit);
    }
}

// Tableau rules for the Symbols experiment.
//   - A single symbol may be placed onto another when it is one tier up and the opposite suit (a
//     run step), or onto a symbol of the same tier (parking).
//   - A multi-card group can only be picked up when it is a strict alternating-suit descending run,
//     and can only be dropped as a run step — you cannot drop a whole ordered run onto a card of its
//     own tier (that would stack a card on top of one from its own tier).
public class SymbolTableauRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
    {
        if (!stack[fromIndex].IsFaceUp) return false;
        for (int i = fromIndex; i < stack.Count - 1; i++)
            if (!IsRunStep(stack[i], stack[i + 1])) return false;
        return true;
    }

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
    {
        if (stack.Count == 0) return true;          // empty column accepts anything

        var top   = stack[^1].CardData.Symbol;
        var below = incoming[0].CardData.Symbol;
        if (top == null || below == null) return false;

        int belowTopTier = stack.Count >= 2 && stack[^2].CardData.Symbol != null ? stack[^2].CardData.Symbol.Tier : -1;

        return SymbolStackRule.CanPlaceOn(
            top.Tier,   (int)stack[^1].CardData.SymbolSuit,
            belowTopTier,
            below.Tier, (int)incoming[0].CardData.SymbolSuit,
            isSingle: incoming.Count == 1);
    }

    // Whether 'upper' continues a strict run on 'lower' (one tier up, opposite suit).
    private static bool IsRunStep(SolitaireCardInstance lower, SolitaireCardInstance upper)
    {
        var below = lower.CardData.Symbol;
        var above = upper.CardData.Symbol;
        if (below == null || above == null) return false;

        return SymbolStackRule.IsRunStep(below.Tier, (int)lower.CardData.SymbolSuit,
                                         above.Tier, (int)upper.CardData.SymbolSuit);
    }
}
