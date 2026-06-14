public enum SolitaireCardRank
{
    Ace,
    Two,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King
}

public static class SolitaireCardRankExtensions
{
    public static string Character(this SolitaireCardRank rank)
    {
        switch (rank)
        {
            case SolitaireCardRank.Ace: return "A";
            case SolitaireCardRank.Two: return "2";
            case SolitaireCardRank.Three: return "3";
            case SolitaireCardRank.Four: return "4";
            case SolitaireCardRank.Five: return "5";
            case SolitaireCardRank.Six: return "6";
            case SolitaireCardRank.Seven: return "7";
            case SolitaireCardRank.Eight: return "8";
            case SolitaireCardRank.Nine: return "9";
            case SolitaireCardRank.Ten: return "10";
            case SolitaireCardRank.Jack: return "J";
            case SolitaireCardRank.Queen: return "Q";
            case SolitaireCardRank.King: return "K";
            default: return "";
        }
    }
}