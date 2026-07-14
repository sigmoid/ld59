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
    // the top has tier belowTopTier (-1 if the top is the only card). lowerSide/upperSide are the
    // runes' sidedness (SymbolDictionary.HorizontalSide: -1 left / 0 centred / +1 right).
    // incomingSameTier = the moving group is all one tier (always true for a single card, true for a
    // same-tier pile moved together).
    //   - a same-tier group may park on a same-tier top, but only when the runes share a side
    //     (a centred rune is a wildcard), and it may do so even onto an existing same-tier pile;
    //   - otherwise it must be a run step, AND the top must not already be part of a same-tier pile
    //     (you can't stack another tier on top of two-or-more cards of the same tier).
    public static bool CanPlaceOn(int lowerTier, int lowerSuit, int lowerSide, int belowTopTier,
                                  int upperTier, int upperSuit, int upperSide, bool incomingSameTier)
    {
        if (incomingSameTier && upperTier == lowerTier)                // same-tier parking...
            return SameSidedness(lowerSide, upperSide);                // ...only when the runes share a side
        if (belowTopTier == lowerTier) return false;                   // top is part of a same-tier pile
        return IsRunStep(lowerTier, lowerSuit, upperTier, upperSuit);
    }

    // Two runes may park on each other when they sit on the same side of their tier row. Sides are
    // -1 (left) / 0 (centred) / +1 (right); a centred rune is a wildcard that pairs with either side.
    public static bool SameSidedness(int lowerSide, int upperSide)
        => lowerSide == 0 || upperSide == 0 || lowerSide == upperSide;
}

// Tableau rules for the Symbols experiment.
//   - A single symbol may be placed onto another when it is one tier up and the opposite suit (a
//     run step), or onto a symbol of the same tier that shares its side of the row (parking).
//   - A multi-card group can be picked up when it is a strict alternating-suit descending run, OR a
//     pile of cards all of the same tier. A run can only be dropped as a run step; a same-tier pile
//     can additionally park on a same-tier top of matching sidedness — but you can never stack a run
//     step onto a card that is itself sitting on one of its own tier.
public class SymbolTableauRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
    {
        if (!stack[fromIndex].IsFaceUp) return false;

        // A strict alternating-suit descending run (also covers a single card).
        bool isRun = true;
        for (int i = fromIndex; i < stack.Count - 1; i++)
            if (!IsRunStep(stack[i], stack[i + 1])) { isRun = false; break; }
        if (isRun) return true;

        // ...or a pile of cards all of the same tier, which moves as a unit.
        return IsSameTierGroup(stack, fromIndex);
    }

    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming)
    {
        if (stack.Count == 0) return true;          // empty column accepts anything

        var top   = stack[^1].CardData.Symbol;
        var below = incoming[0].CardData.Symbol;
        if (top == null || below == null) return false;

        int belowTopTier = stack.Count >= 2 && stack[^2].CardData.Symbol != null ? stack[^2].CardData.Symbol.Tier : -1;

        return SymbolStackRule.CanPlaceOn(
            top.Tier,   (int)stack[^1].CardData.SymbolSuit,      SymbolDictionary.HorizontalSide(top),
            belowTopTier,
            below.Tier, (int)incoming[0].CardData.SymbolSuit,    SymbolDictionary.HorizontalSide(below),
            incomingSameTier: IsSameTierGroup(incoming, 0));
    }

    // Whether every card from fromIndex to the top shares a tier (a same-tier pile / single card).
    private static bool IsSameTierGroup(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex)
    {
        var baseSymbol = stack[fromIndex].CardData.Symbol;
        if (baseSymbol == null) return false;
        for (int i = fromIndex + 1; i < stack.Count; i++)
        {
            var symbol = stack[i].CardData.Symbol;
            if (symbol == null || symbol.Tier != baseSymbol.Tier) return false;
        }
        return true;
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
