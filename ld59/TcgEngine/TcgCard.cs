using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A card in play or in hand: a stack of symbols. Under the v3 attrition rules (tcg.md) the
/// symbols ARE the card — they are its hit points (Length), and all stats recompute from whatever
/// symbols remain, so damage genuinely degrades a card. Tier is the peak; sidedness is the balance.
/// </summary>
public class TcgCard
{
    public List<Symbol> Symbols;
    public Word Word;                    // dictionary tag when the symbols match a known word
    public bool HasAttackedThisTurn;

    public int Tier => Symbols.Count == 0 ? 0 : Symbols.Max(s => s.Tier);
    public int Sidedness => Symbols.Count == 0 ? 0 : System.Math.Sign(Symbols.Sum(s => SymbolDictionary.HorizontalSide(s)));
    public int Length => Symbols.Count;
    public string DisplayName => Word?.Name ?? (Symbols.Count > 0 ? Symbols[0].Name : "(spent)");

    public static TcgCard FromSymbol(Symbol s) => new() { Symbols = new List<Symbol> { s } };
    public static TcgCard FromWord(Word w) => new() { Symbols = w.Symbols.ToList(), Word = w };

    /// <summary>A free-form combined card from a fusion. Stats are derived from the symbol union;
    /// if the combination happens to be a known dictionary word it is tagged as such (giving it a
    /// name now and a hook for the planned special-effect words later).</summary>
    public static TcgCard FromSymbols(IEnumerable<Symbol> symbols)
    {
        var list = symbols.ToList();
        return new TcgCard { Symbols = list, Word = WordDictionary.Match(list) };
    }

    /// <summary>
    /// Applies combat damage from an attacker of the given tier and stack size.
    /// Higher-tier attackers still knock the peak symbol off.
    /// Equal-tier collisions first try to shed the lowest-tier symbol that is strictly below the
    /// attacker; if none exists, the larger stack wins and the smaller one survives.
    /// Returns true when the card is emptied; the word tag re-derives.
    /// </summary>
    public bool TakeHit(int attackerTier, int attackerLength)
    {
        if (Symbols.Count > 0)
        {
            if (attackerTier > Tier)
            {
                int fall = 0;
                for (int i = 1; i < Symbols.Count; i++)
                    if (Symbols[i].Tier >= Symbols[fall].Tier) fall = i;   // >= : later index wins ties
                Symbols.RemoveAt(fall);
            }
            else if (attackerTier == Tier)
            {
                int fall = -1;
                for (int i = 0; i < Symbols.Count; i++)
                {
                    if (Symbols[i].Tier < attackerTier &&
                        (fall < 0 || Symbols[i].Tier < Symbols[fall].Tier ||
                         (Symbols[i].Tier == Symbols[fall].Tier && i > fall)))
                    {
                        fall = i;
                    }
                }

                if (fall >= 0) Symbols.RemoveAt(fall);
                else if (attackerLength >= Length) Symbols.Clear();
                else return false;
            }
        }
        Word = Symbols.Count > 0 ? WordDictionary.Match(Symbols) : null;
        return Symbols.Count == 0;
    }
}
