public enum TcgPhase { Main, Attack }

/// <summary>
/// Full game state. Pure data — mutated only by TcgGame, queried by TcgRules/TcgAi/UI.
/// Player 0 is the human, player 1 the AI (the engine itself doesn't care).
/// </summary>
public class TcgGameState
{
    public const int FrontSlots = 3;
    public const int LifeSlots = 3;
    public const int StartingHand = 5;
    // 3 copies (45-card decks): with 2, AI-vs-AI sims showed ~10% of games exhausting the
    // symbol pool before either side could build a counter to the last life card.
    public const int CopiesPerSymbol = 3;
    public const int SummonLimit = 2;    // per turn, fusion included

    public TcgPlayerState[] Players = { new TcgPlayerState(), new TcgPlayerState() };
    public int CurrentPlayer;
    public TcgPhase Phase = TcgPhase.Main;
    public int TurnNumber = 1;           // whoever acts on turn 1 may not attack (tcg.md)
    public int SummonsUsed;
    public bool FusionUsed;
    public int Winner = -1;              // -1 = game in progress

    public TcgPlayerState Current => Players[CurrentPlayer];
    public TcgPlayerState Opponent => Players[1 - CurrentPlayer];
}
