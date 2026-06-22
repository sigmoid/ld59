// Two "suits" for symbol cards: Light = white card / black symbol, Dark = black card / white symbol.
public enum SymbolSuit
{
    Light,
    Dark,
}

public class SolitaireCardData
{
    public SolitaireCardSuit Suit;
    public SolitaireCardRank Rank;

    // When set, this card represents an alien alphabet symbol instead of a suit/rank.
    public Symbol Symbol;

    // Which symbol suit (light/dark) this card is drawn as.
    public SymbolSuit SymbolSuit;
}