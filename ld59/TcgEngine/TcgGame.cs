using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// The mutation engine: owns a TcgGameState and applies legal actions to it. All legality
/// checks defer to TcgRules; every mutator returns false (state untouched) on an illegal action.
/// Seeded so headless tests are reproducible.
/// </summary>
public class TcgGame
{
    public TcgGameState State { get; } = new TcgGameState();

    private readonly System.Random _rng;   // System-qualified: the game defines its own static Random

    public TcgGame(int seed)
    {
        Debug.Assert(WordDictionary.ValidationErrors().Count == 0,
            "Word list failed validation: " + string.Join("; ", WordDictionary.ValidationErrors()));

        _rng = new System.Random(seed);
        foreach (var p in State.Players)
        {
            foreach (var sym in SymbolDictionary.All)
                for (int i = 0; i < TcgGameState.CopiesPerSymbol; i++)
                    p.Deck.Add(sym);
            Shuffle(p.Deck);

            for (int i = 0; i < TcgGameState.StartingHand; i++)
                Draw(p);

            // 3 distinct random life words per player (players may share words with each other).
            var pool = WordDictionary.All.Where(w => w.CanBeLife).ToList();
            for (int i = 0; i < TcgGameState.LifeSlots; i++)
            {
                int pick = _rng.Next(pool.Count);
                p.LifeRow[i] = TcgCard.FromWord(pool[pick]);
                pool.RemoveAt(pick);
            }
        }

        Draw(State.Current);   // every turn starts with a draw, including the very first
    }

    public bool Apply(TcgAction a) => a.Kind switch
    {
        TcgActionKind.Summon => Summon(a),
        TcgActionKind.Fuse => Fuse(a),
        TcgActionKind.Attack => Attack(a),
        TcgActionKind.EndTurn => EndTurn(),
        _ => false,
    };

    public bool Summon(TcgAction a)
    {
        if (!TcgRules.CanSummon(State, a.SourceIndex, a.TargetIndex)) return false;
        var p = State.Current;
        p.FrontRow[a.TargetIndex] = p.Hand[a.SourceIndex];
        p.Hand.RemoveAt(a.SourceIndex);
        State.SummonsUsed++;
        return true;
    }

    public bool Fuse(TcgAction a)
    {
        if (!TcgRules.CanFuse(State, a)) return false;
        var p = State.Current;

        // Both participants are on the field. The combined card is the union of their symbols and
        // lands in the first-selected slot; the second slot empties. Fusion is its own once-per-turn
        // action and does not spend a summon.
        var combined = TcgCard.FromSymbols(p.FrontRow[a.SourceIndex].Symbols.Concat(p.FrontRow[a.TargetIndex].Symbols));
        p.FrontRow[a.SourceIndex] = combined;
        p.FrontRow[a.TargetIndex] = null;

        State.FusionUsed = true;
        return true;
    }

    public bool Attack(TcgAction a)
    {
        if (!TcgRules.CanAttack(State, a)) return false;

        State.Phase = TcgPhase.Attack;   // first attack ends the main phase for this turn

        var attacker = State.Current.FrontRow[a.SourceIndex];
        var target = TcgRules.GetEnemy(State, a.TargetZone, a.TargetIndex);
        attacker.HasAttackedThisTurn = true;

        bool mutual = TcgRules.IsMutual(attacker, target);
        bool targetDestroyed = target.TakeHit(attacker.Tier, attacker.Length);
        if (mutual && targetDestroyed)
        {
            State.Current.FrontRow[a.SourceIndex] = null;
            State.Current.CardsLost++;
        }

        if (targetDestroyed)
        {
            if (a.TargetZone == TcgZone.Front) State.Opponent.FrontRow[a.TargetIndex] = null;
            else State.Opponent.LifeRow[a.TargetIndex] = null;
            State.Opponent.CardsLost++;
            if (!State.Opponent.HasLife)
                State.Winner = State.CurrentPlayer;
        }
        return true;
    }

    public bool EndTurn()
    {
        if (State.Winner >= 0) return false;
        State.CurrentPlayer = 1 - State.CurrentPlayer;
        State.TurnNumber++;
        State.SummonsUsed = 0;
        State.FusionUsed = false;
        State.Phase = TcgPhase.Main;
        foreach (var card in State.Current.FrontRow)
            if (card != null) card.HasAttackedThisTurn = false;
        Draw(State.Current);
        return true;
    }

    private void Draw(TcgPlayerState p)
    {
        if (p.Deck.Count == 0) return;   // empty deck just stops drawing — no deck-out loss
        p.Hand.Add(TcgCard.FromSymbol(p.Deck[^1]));
        p.Deck.RemoveAt(p.Deck.Count - 1);
    }

    private void Shuffle(List<Symbol> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }
}
