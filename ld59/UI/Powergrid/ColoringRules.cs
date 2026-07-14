using System;

namespace ld59.UI.Powergrid;

/// <summary>
/// A constraint on which runes two <b>adjacent</b> nodes may simultaneously hold. A level enables a
/// set of these; an edge is in conflict if its two runes violate any active rule.
/// </summary>
public enum ColoringRule
{
    /// <summary>Adjacent nodes must hold different runes (the base graph-colouring constraint).</summary>
    DifferentRune,

    /// <summary>Adjacent nodes must hold runes from different alphabet tiers. (Kept for reference;
    /// not currently surfaced as a toggle.)</summary>
    DifferentTier,

    /// <summary>For adjacent nodes of <b>different</b> tiers, the tiers must be exactly one apart.
    /// Says nothing about same-tier pairs (those are governed by <see cref="Sidedness"/> /
    /// <see cref="DifferentRune"/>), so it composes cleanly with them.</summary>
    TierStep,

    /// <summary>Within a tier, adjacent runes must sit on the same side of the row (left or right by
    /// <see cref="Symbol.RowOrder"/>). A centred rune (odd-length row, e.g. Chi) pairs with either
    /// side. Runes of different tiers are unconstrained by this rule.</summary>
    Sidedness,
}

public static class ColoringRules
{
    /// <summary>True if this adjacent pairing breaks the given rule.</summary>
    public static bool Violates(ColoringRule rule, Symbol a, Symbol b) => rule switch
    {
        ColoringRule.DifferentRune => a.Name == b.Name,
        ColoringRule.DifferentTier => a.Tier == b.Tier,
        ColoringRule.TierStep      => a.Tier != b.Tier && System.Math.Abs(a.Tier - b.Tier) != 1,
        ColoringRule.Sidedness     => OppositeSidesOfTier(a, b),
        _ => false,
    };

    private enum Side { Left, Center, Right }

    /// <summary>Horizontal position of a rune within its tier row: -1 = left of centre, +1 = right of
    /// centre, 0 = centred (only when the row has an odd rune count). Defined by the alphabet itself
    /// (<see cref="SymbolDictionary.HorizontalSide"/>) so sidedness is the same everywhere it's used.</summary>
    public static int HorizontalSide(Symbol s) => SymbolDictionary.HorizontalSide(s);

    /// <summary>A rune's side within its row: left/right of the row centre, or centre (wildcard).</summary>
    private static Side SideOf(Symbol s)
    {
        int h = HorizontalSide(s);
        return h < 0 ? Side.Left : h > 0 ? Side.Right : Side.Center;
    }

    /// <summary>Same tier and opposite definite sides (one Left, one Right). A centred rune never
    /// counts as a definite side, so it pairs with either.</summary>
    private static bool OppositeSidesOfTier(Symbol a, Symbol b)
    {
        if (a.Tier != b.Tier) return false;
        var sa = SideOf(a);
        var sb = SideOf(b);
        return sa != Side.Center && sb != Side.Center && sa != sb;
    }

    /// <summary>Requirement clause for the status line, e.g. "be one tier apart".</summary>
    public static string Hint(ColoringRule rule) => rule switch
    {
        ColoringRule.DifferentRune => "hold different runes",
        ColoringRule.DifferentTier => "hold different tiers",
        ColoringRule.TierStep      => "be one tier apart (across tiers)",
        ColoringRule.Sidedness     => "be the same side (within a tier)",
        _ => "",
    };

    /// <summary>Short label for the editor toggle.</summary>
    public static string ShortName(ColoringRule rule) => rule switch
    {
        ColoringRule.DifferentRune => "Rune",
        ColoringRule.DifferentTier => "Tier",
        ColoringRule.TierStep      => "Step",
        ColoringRule.Sidedness     => "Side",
        _ => rule.ToString(),
    };

    /// <summary>Parses a rule from either its full enum name ("DifferentRune") or the compact short
    /// name used in serialized blobs ("Rune"). Both are accepted so connection-override round-trips
    /// (which write <see cref="ShortName"/>) survive a save/load cycle.</summary>
    public static bool TryParse(string s, out ColoringRule rule)
    {
        if (Enum.TryParse(s, ignoreCase: true, out rule)) return true;
        switch (s?.Trim().ToLowerInvariant())
        {
            case "rune": rule = ColoringRule.DifferentRune; return true;
            case "tier": rule = ColoringRule.DifferentTier; return true;
            case "step": rule = ColoringRule.TierStep;      return true;
            case "side": rule = ColoringRule.Sidedness;     return true;
        }
        rule = default;
        return false;
    }
}
