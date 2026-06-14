using System.Collections.Generic;
using Microsoft.Xna.Framework;

public enum SolitaireCardSuit
{
    Hearts,
    Diamonds,
    Clubs,
    Spades
}

public static class SolitaireCardSuitExtensions
{
    private static Dictionary<SolitaireCardSuit, string> SuitTexturePaths = new Dictionary<SolitaireCardSuit, string>
    {
        { SolitaireCardSuit.Hearts, "images/solitaire/Heart-Unfilled" },
        { SolitaireCardSuit.Diamonds, "images/solitaire/Diamond-Unfilled" },
        { SolitaireCardSuit.Clubs, "images/solitaire/Clubs-Filled" },
        { SolitaireCardSuit.Spades, "images/solitaire/Spade-Filled" }
    };

    public static string TexturePath(this SolitaireCardSuit suit)
    {
        return SuitTexturePaths[suit];
    }

    public static Color SuitColor(this SolitaireCardSuit suit)
    {
        return suit switch
        {
            SolitaireCardSuit.Hearts   => Color.Black,
            SolitaireCardSuit.Diamonds => Color.Black,
            SolitaireCardSuit.Clubs    => Color.Black,
            SolitaireCardSuit.Spades   => Color.Black,
            _ => Color.Black
        };
    }

}
