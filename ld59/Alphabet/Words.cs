using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A word in the alien language: an unordered combination of alphabet symbols.
/// Words are the TCG's fusion targets and life cards (see tcg.md). Stats are derived
/// from the constituent symbols, never hand-tuned.
/// </summary>
public class Word
{
    public string Name;
    public string Meaning;          // English gloss; doubles as the card's description text
    public Symbol[] Symbols;

    // Apex words with no counter in the dictionary must opt out of being dealt as life cards,
    // or a game could start unwinnable. Checked by WordDictionary.ValidationErrors().
    public bool CanBeLife = true;

    public int Tier => Symbols.Max(s => s.Tier);
    public int Sidedness => System.Math.Sign(Symbols.Sum(s => SymbolDictionary.HorizontalSide(s)));
    public int Length => Symbols.Length;
}

/// <summary>
/// The shared dictionary of valid words (tcg.md: the "extra deck"). Inexhaustible — summoning
/// a word never removes it. The list is hand-authored; ValidationErrors() enforces the
/// constraints that keep the fusion ladder playable and is asserted empty by TcgSimTests.
/// </summary>
public static class WordDictionary
{
    public static readonly List<Word> All = new()
    {
        // 2-symbol words. Balance note: every word needs several counter routes that stay
        // buildable late-game (only 2 copies of each symbol per deck) — AI-vs-AI sims stalemate
        // when a life card's counters all funnel through one exhausted symbol.
        new Word { Name = "Liphi",   Meaning = "dawn",     Symbols = new[] { SymbolDictionary.Lith,   SymbolDictionary.Phi } },
        new Word { Name = "Laxe",    Meaning = "dusk",     Symbols = new[] { SymbolDictionary.Lith,   SymbolDictionary.Axe } },
        new Word { Name = "Phiom",   Meaning = "river",    Symbols = new[] { SymbolDictionary.Phi,    SymbolDictionary.Omam } },
        new Word { Name = "Axmed",   Meaning = "mountain", Symbols = new[] { SymbolDictionary.Axe,    SymbolDictionary.Medal } },
        new Word { Name = "Lisq",    Meaning = "tide",     Symbols = new[] { SymbolDictionary.Lith,   SymbolDictionary.Squid } },
        new Word { Name = "Mophi",   Meaning = "night",    Symbols = new[] { SymbolDictionary.Moon,   SymbolDictionary.Phi } },
        new Word { Name = "Kax",     Meaning = "day",      Symbols = new[] { SymbolDictionary.Kite,   SymbolDictionary.Axe } },
        new Word { Name = "Mumon",   Meaning = "abyss",    Symbols = new[] { SymbolDictionary.Mouth,  SymbolDictionary.Moon } },
        new Word { Name = "Takite",  Meaning = "zenith",   Symbols = new[] { SymbolDictionary.Target, SymbolDictionary.Kite } },
        new Word { Name = "Quilk",   Meaning = "devourer", Symbols = new[] { SymbolDictionary.Chi,    SymbolDictionary.Squid } },
        // Every symbol appears in at least one word — a symbol with no fusion use is dead weight
        // that clogs the board (AI-vs-AI sims stalemate on it).
        new Word { Name = "Homam",   Meaning = "shade",    Symbols = new[] { SymbolDictionary.Horns,  SymbolDictionary.Omam } },
        new Word { Name = "Kumed",   Meaning = "ridge",    Symbols = new[] { SymbolDictionary.Kurt,   SymbolDictionary.Medal } },
        new Word { Name = "Dorn",    Meaning = "spear",    Symbols = new[] { SymbolDictionary.D,      SymbolDictionary.Horns } },
        new Word { Name = "Eykur",   Meaning = "sentinel", Symbols = new[] { SymbolDictionary.Eye,    SymbolDictionary.Kurt } },

        // 3-symbol words (each contains a 2-word above, so the fusion ladder can reach it).
        // Phiomo/Axmeki deliberately avoid Lith so tier-4 life cards stay counterable after
        // both Liths leave the game.
        new Word { Name = "Liphiom", Meaning = "flood",    Symbols = new[] { SymbolDictionary.Lith, SymbolDictionary.Phi,   SymbolDictionary.Omam } },
        new Word { Name = "Laxmed",  Meaning = "stone",    Symbols = new[] { SymbolDictionary.Lith, SymbolDictionary.Axe,   SymbolDictionary.Medal } },
        new Word { Name = "Liphimo", Meaning = "eclipse",  Symbols = new[] { SymbolDictionary.Lith, SymbolDictionary.Phi,   SymbolDictionary.Moon } },
        new Word { Name = "Laxkite", Meaning = "storm",    Symbols = new[] { SymbolDictionary.Lith, SymbolDictionary.Axe,   SymbolDictionary.Kite } },
        new Word { Name = "Phiomo",  Meaning = "delta",    Symbols = new[] { SymbolDictionary.Phi,  SymbolDictionary.Omam,  SymbolDictionary.Moon } },
        new Word { Name = "Axmeki",  Meaning = "summit",   Symbols = new[] { SymbolDictionary.Axe,  SymbolDictionary.Medal, SymbolDictionary.Kite } },
        // Tier-5 3-words: the only clean (non-mutual) counters to tier-4 3-word life cards.
        // Not life-eligible themselves — nothing outranks tier 5, so games where both sides
        // draw them as life cards grind into mutual-only endgames.
        new Word { Name = "Quilom",  Meaning = "maw",      Symbols = new[] { SymbolDictionary.Chi,  SymbolDictionary.Squid, SymbolDictionary.Omam },  CanBeLife = false },
        new Word { Name = "Quilmed", Meaning = "jaws",     Symbols = new[] { SymbolDictionary.Chi,  SymbolDictionary.Squid, SymbolDictionary.Medal }, CanBeLife = false },
    };

    /// <summary>The word whose symbols equal the given multiset (order-insensitive), or null.
    /// This is the fusion lookup: two cards fuse iff their combined symbols Match a word.</summary>
    public static Word Match(IEnumerable<Symbol> symbols)
    {
        var key = symbols.ToList();
        foreach (var w in All)
            if (SameMultiset(w.Symbols, key))
                return w;
        return null;
    }

    /// <summary>"L", "C", or "R" for a -1/0/+1 sidedness value. Shared by card faces and the
    /// dictionary browser.</summary>
    public static string SideGlyph(int side) => side < 0 ? "L" : side > 0 ? "R" : "C";

    /// <summary>Authoring sanity checks. Empty list = valid. Asserted by TcgSimTests and
    /// debug-asserted when a game starts. Under the v2 attrition rules (tcg.md) words no longer
    /// gate fusion and need no exact counters — any card can be chipped down — so the old
    /// reachability/counterability constraints are gone; only a copies cap remains so no word
    /// (or future effect combination) demands more duplicates than a deck can supply.
    /// Pass a list to validate a candidate word set (used by tests); defaults to the dictionary.</summary>
    public static List<string> ValidationErrors(List<Word> words = null)
    {
        var all = words ?? All;
        var errors = new List<string>();

        foreach (var w in all)
            foreach (var g in w.Symbols.GroupBy(s => s))
                if (g.Count() > 2)
                    errors.Add($"{w.Name}: uses {g.Count()}x {g.Key.Name} — words may use at most 2 copies of a symbol");

        return errors;
    }

    private static bool SameMultiset(IReadOnlyCollection<Symbol> a, IReadOnlyCollection<Symbol> b) =>
        a.Count == b.Count && IsSubMultiset(a, b);

    /// <summary>True when every symbol of inner appears in outer at least as many times.
    /// Public because the AI uses it to recognize fusion-ladder steps toward a goal word.</summary>
    public static bool IsSubMultiset(IEnumerable<Symbol> inner, IEnumerable<Symbol> outer)
    {
        var counts = new Dictionary<Symbol, int>();
        foreach (var s in outer)
            counts[s] = counts.GetValueOrDefault(s) + 1;
        foreach (var s in inner)
        {
            if (counts.GetValueOrDefault(s) == 0) return false;
            counts[s]--;
        }
        return true;
    }
}
