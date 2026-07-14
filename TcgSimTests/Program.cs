using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Headless assertions for the TCG minigame (tcg.md). Covers the word dictionary's authoring
/// constraints, the rules engine's engagement/fusion/turn logic, and AI-vs-AI full games.
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(string desc, bool cond)
    {
        if (cond) { _passed++; Console.WriteLine($"  PASS  {desc}"); }
        else { _failed++; Console.WriteLine($"  FAIL  {desc}"); }
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "diag") { DiagnoseStalls(); return 0; }
        if (args.Length > 0 && args[0] == "trace") { TraceGame(args.Length > 1 ? int.Parse(args[1]) : 0); return 0; }
        if (args.Length > 0 && args[0] == "winrate") { WinRate(args.Length > 1 ? int.Parse(args[1]) : 1000); return 0; }

        Console.WriteLine("TCG engine tests\n");

        WordStats();
        WordMatching();
        WordValidation();
        TierGate();
        ChipMechanics();
        SummonRules();
        FusionRules();
        AttackResolution();
        LifeAndWin();
        TurnFlow();
        AiFullGames();
        AiIgnoresEnemyHand();

        Console.WriteLine($"\n{_passed}/{_passed + _failed} passed.");
        return _failed == 0 ? 0 : 1;
    }

    private static Word W(string name) => WordDictionary.All.First(w => w.Name == name);

    // ── Words ──────────────────────────────────────────────────────────────────

    private static void WordStats()
    {
        Console.WriteLine("Word derived stats");
        Check("Liphi is tier 2, left, length 2", W("Liphi").Tier == 2 && W("Liphi").Sidedness == -1 && W("Liphi").Length == 2);
        Check("Laxe is tier 2, right", W("Laxe").Tier == 2 && W("Laxe").Sidedness == 1);
        Check("Lisq is tier 3, center", W("Lisq").Tier == 3 && W("Lisq").Sidedness == 0);
        Check("Quilk is tier 5, center", W("Quilk").Tier == 5 && W("Quilk").Sidedness == 0);
        Check("Mumon/Takite are the tier-5 side pair", W("Mumon").Tier == 5 && W("Mumon").Sidedness == -1 && W("Takite").Tier == 5 && W("Takite").Sidedness == 1);
        Check("Laxkite is tier 4, right, length 3", W("Laxkite").Tier == 4 && W("Laxkite").Sidedness == 1 && W("Laxkite").Length == 3);
    }

    private static void WordMatching()
    {
        Console.WriteLine("Fusion lookup (multiset match)");
        Check("Lith+Phi matches Liphi", WordDictionary.Match(new[] { SymbolDictionary.Lith, SymbolDictionary.Phi }) == W("Liphi"));
        Check("Phi+Lith matches Liphi (order-insensitive)", WordDictionary.Match(new[] { SymbolDictionary.Phi, SymbolDictionary.Lith }) == W("Liphi"));
        Check("Lith+Lith matches nothing", WordDictionary.Match(new[] { SymbolDictionary.Lith, SymbolDictionary.Lith }) == null);
        Check("Lith+Phi+Omam matches Liphiom", WordDictionary.Match(new[] { SymbolDictionary.Lith, SymbolDictionary.Phi, SymbolDictionary.Omam }) == W("Liphiom"));
        Check("Lith alone matches nothing", WordDictionary.Match(new[] { SymbolDictionary.Lith }) == null);
    }

    private static void WordValidation()
    {
        Console.WriteLine("Word list validation");
        var errors = WordDictionary.ValidationErrors();
        Check("shipping word list is valid" + (errors.Count > 0 ? " [" + string.Join("; ", errors) + "]" : ""), errors.Count == 0);

        var tripleLith = new Word { Name = "X", Symbols = new[] { SymbolDictionary.Lith, SymbolDictionary.Lith, SymbolDictionary.Lith }, CanBeLife = false };
        Check("3 copies of a symbol fails the copies cap",
            WordDictionary.ValidationErrors(new List<Word> { tripleLith }).Any(e => e.Contains("copies")));
    }

    // ── Rules ──────────────────────────────────────────────────────────────────

    private static TcgCard C(Symbol s) => TcgCard.FromSymbol(s);
    private static TcgCard C(string word) => TcgCard.FromWord(W(word));

    private static void TierGate()
    {
        Console.WriteLine("Combat legality by tier (sidedness irrelevant)");
        Check("a higher card can damage a lower one (Phi over Lith)", TcgRules.CanDamage(C(SymbolDictionary.Phi), C(SymbolDictionary.Lith)));
        Check("equal tiers can attack (Moon vs Horns)", TcgRules.CanDamage(C(SymbolDictionary.Moon), C(SymbolDictionary.Horns)));
        Check("equal tiers are a mutual trade", TcgRules.IsMutual(C(SymbolDictionary.Moon), C(SymbolDictionary.Horns)));
        Check("a higher card is not a mutual trade", !TcgRules.IsMutual(C(SymbolDictionary.Phi), C(SymbolDictionary.Lith)));
        Check("a lower card cannot damage a higher one (Lith vs Moon)", !TcgRules.CanDamage(C(SymbolDictionary.Lith), C(SymbolDictionary.Moon)));
        Check("sidedness does not matter (Phi vs Axe, equal tier)", TcgRules.CanDamage(C(SymbolDictionary.Phi), C(SymbolDictionary.Axe)));
        Check("a lower word cannot reach a higher word (Liphi vs Phiom)", !TcgRules.CanDamage(C("Liphi"), C("Phiom")));
    }

    private static void ChipMechanics()
    {
        Console.WriteLine("Symbol damage (a hit always removes the peak symbol)");

        var peak = C("Liphiom");   // Lith(1) + Phi(2) + Omam(3)
        Check("a hit knocks off the top symbol", !peak.TakeHit() && peak.Length == 2 && peak.Tier == 2);
        Check("the survivors re-derive their word tag", peak.Word == W("Liphi"));

        var tie = TcgCard.FromSymbols(new[] { SymbolDictionary.Phi, SymbolDictionary.Axe });   // both tier 2
        Check("tier ties: most recently added falls (removes Axe)",
            !tie.TakeHit() && tie.Symbols[0] == SymbolDictionary.Phi && tie.Sidedness == -1);

        var small = C("Liphi");
        Check("emptying the last symbol destroys the card", !small.TakeHit() && small.TakeHit());
    }

    // A rigged mid-game state: both boards/hands empty, each side holding one life card,
    // turn 3 (attacks legal), player 0 to act.
    private static TcgGame Rigged()
    {
        var g = new TcgGame(seed: 1);
        var s = g.State;
        foreach (var p in s.Players)
        {
            p.Hand.Clear();
            Array.Clear(p.FrontRow);
            Array.Clear(p.LifeRow);
            p.LifeRow[0] = TcgCard.FromWord(W("Mophi"));
        }
        s.CurrentPlayer = 0;
        s.TurnNumber = 3;
        s.Phase = TcgPhase.Main;
        s.SummonsUsed = 0;
        s.FusionUsed = false;
        return g;
    }

    private static void SummonRules()
    {
        Console.WriteLine("Summoning");
        var g = Rigged();
        var s = g.State;
        s.Current.Hand.Add(C(SymbolDictionary.Eye));
        s.Current.Hand.Add(C(SymbolDictionary.Moon));
        s.Current.Hand.Add(C(SymbolDictionary.Lith));

        Check("summon to empty slot succeeds", g.Summon(TcgAction.Summon(0, 0)) && s.Current.FrontRow[0] != null && s.Current.Hand.Count == 2);
        Check("summon to occupied slot fails", !g.Summon(TcgAction.Summon(0, 0)));
        Check("second summon succeeds", g.Summon(TcgAction.Summon(0, 1)));
        Check("third summon fails (limit 2)", !g.Summon(TcgAction.Summon(0, 2)));

        var g2 = Rigged();
        g2.State.Current.FrontRow[0] = C("Phiom");
        g2.State.Opponent.FrontRow[0] = C("Liphi");
        g2.State.Current.Hand.Add(C(SymbolDictionary.Lith));
        Check("attack ends the main phase", g2.Attack(TcgAction.Attack(0, TcgZone.Front, 0)) && !g2.Summon(TcgAction.Summon(0, 1)));
    }

    private static void FusionRules()
    {
        Console.WriteLine("Fusion (summoned cards, same school, tiers one apart)");

        // Lith(T1 centre) + Phi(T2 left): adjacent tiers, compatible side → combines into slot 0.
        var g = Rigged();
        var s = g.State;
        s.Current.FrontRow[0] = C(SymbolDictionary.Lith);    // tier 1, centre
        s.Current.FrontRow[2] = C(SymbolDictionary.Phi);     // tier 2, left
        Check("adjacent-tier fusion combines into the first slot",
            g.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 2)) &&
            s.Current.FrontRow[0] != null && s.Current.FrontRow[0].Length == 2 && s.Current.FrontRow[2] == null);
        Check("combined card derives its stats", s.Current.FrontRow[0].Tier == 2 && s.Current.FrontRow[0].Sidedness == -1);
        Check("a known combination is tagged with its word", s.Current.FrontRow[0].Word == W("Liphi"));
        Check("fusion does not spend a summon", s.SummonsUsed == 0);
        Check("only one fusion per turn (Liphi T2 + Omam T3, otherwise legal)",
            (s.Current.FrontRow[1] = C(SymbolDictionary.Omam)) != null &&
            !g.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 1)));

        // Non-dictionary but adjacent + same side still fuses (Phi T2 + Omam T3, both left).
        var g2 = Rigged();
        g2.State.Current.FrontRow[0] = C(SymbolDictionary.Phi);
        g2.State.Current.FrontRow[1] = C(SymbolDictionary.Omam);
        Check("non-word adjacent same-side combination still fuses",
            g2.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 1)) &&
            g2.State.Current.FrontRow[0] != null && g2.State.Current.FrontRow[0].Length == 2);

        // Opposite sides cannot combine even when tiers are adjacent (Phi T2 L + Medal T3 R).
        var g3 = Rigged();
        g3.State.Current.FrontRow[0] = C(SymbolDictionary.Phi);
        g3.State.Current.FrontRow[1] = C(SymbolDictionary.Medal);
        Check("opposite-side cards cannot fuse", !g3.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 1)));

        // Centre is a fusion wildcard (Squid T3 C + Axe T2 R: adjacent, centre bridges).
        var g4 = Rigged();
        g4.State.Current.FrontRow[0] = C(SymbolDictionary.Squid);
        g4.State.Current.FrontRow[1] = C(SymbolDictionary.Axe);
        Check("centre pairs with either side", g4.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 1)));

        // Equal tiers cannot fuse (Moon T4 L + Horns T4 L: same side, but tiers not one apart).
        var g5 = Rigged();
        g5.State.Current.FrontRow[0] = C(SymbolDictionary.Moon);
        g5.State.Current.FrontRow[1] = C(SymbolDictionary.Horns);
        Check("equal-tier cards cannot fuse", !g5.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 1)));

        // Tiers two apart cannot fuse (Lith T1 C + Squid T3 C: both centre, but a two-step jump).
        var g6 = Rigged();
        g6.State.Current.FrontRow[0] = C(SymbolDictionary.Lith);
        g6.State.Current.FrontRow[1] = C(SymbolDictionary.Squid);
        Check("tiers two apart cannot fuse", !g6.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Front, 1)));

        // Hand cards may not fuse — only summoned cards.
        var g7 = Rigged();
        g7.State.Current.FrontRow[0] = C(SymbolDictionary.Lith);
        g7.State.Current.Hand.Add(C(SymbolDictionary.Phi));
        Check("hand cards cannot fuse (summoned only)", !g7.Fuse(TcgAction.Fuse(TcgZone.Front, 0, TcgZone.Hand, 0)));
    }

    private static void AttackResolution()
    {
        Console.WriteLine("Attack resolution (tier-gated; peak chip or mutual trade)");

        // Higher tier, length-1 target: one chip empties it and the attacker survives.
        var g = Rigged();
        var s = g.State;
        s.Current.FrontRow[0] = C(SymbolDictionary.Phi);     // tier 2
        s.Opponent.FrontRow[0] = C(SymbolDictionary.Lith);   // tier 1, length 1 → chipped away
        Check("a chip that empties the target destroys it; the attacker survives",
            g.Attack(TcgAction.Attack(0, TcgZone.Front, 0)) &&
            s.Opponent.FrontRow[0] == null && s.Current.FrontRow[0] != null && s.Opponent.CardsLost == 1);
        Check("a card attacks only once per turn",
            (s.Opponent.FrontRow[1] = C(SymbolDictionary.Lith)) != null && !g.Attack(TcgAction.Attack(0, TcgZone.Front, 1)));

        // Higher tier vs a multi-symbol card: the peak falls, blunting its tier; attacker survives.
        var g2 = Rigged();
        g2.State.Current.FrontRow[0] = C("Quilom");        // tier 5
        g2.State.Opponent.FrontRow[0] = C("Laxkite");      // Lith1 + Axe2 + Kite4, tier 4, length 3
        Check("a higher card chips the target's peak and survives",
            g2.Attack(TcgAction.Attack(0, TcgZone.Front, 0)) &&
            g2.State.Current.FrontRow[0] != null &&
            g2.State.Opponent.FrontRow[0].Length == 2 && g2.State.Opponent.FrontRow[0].Tier == 2);

        // Equal tier: both cards are destroyed.
        var g3 = Rigged();
        g3.State.Current.FrontRow[0] = C("Phiom");         // tier 3
        g3.State.Opponent.FrontRow[0] = C("Laxmed");       // tier 3, length 3
        Check("equal tiers destroy each other outright",
            g3.Attack(TcgAction.Attack(0, TcgZone.Front, 0)) &&
            g3.State.Current.FrontRow[0] == null && g3.State.Opponent.FrontRow[0] == null &&
            g3.State.Current.CardsLost == 1 && g3.State.Opponent.CardsLost == 1);

        // Lower tier cannot attack a higher one at all.
        var g4 = Rigged();
        g4.State.Current.FrontRow[0] = C("Phiom");         // tier 3
        g4.State.Opponent.FrontRow[0] = C("Laxkite");      // tier 4
        Check("a lower card cannot attack a higher one",
            !g4.Attack(TcgAction.Attack(0, TcgZone.Front, 0)) && g4.State.Opponent.FrontRow[0] != null);

        var g5 = Rigged();
        g5.State.TurnNumber = 1;
        g5.State.Current.FrontRow[0] = C("Phiom");
        g5.State.Opponent.FrontRow[0] = C(SymbolDictionary.Lith);
        Check("no attacks on turn 1", !g5.Attack(TcgAction.Attack(0, TcgZone.Front, 0)));
    }

    private static void LifeAndWin()
    {
        Console.WriteLine("Life cards and winning");

        // Reach: the life row is shielded while ANY card is on the defender's front row, no matter
        // how big the attacker is. The board must be cleared first.
        var g = Rigged();
        var s = g.State;
        s.Opponent.LifeRow[0] = C("Liphi");                  // tier 2
        s.Opponent.FrontRow[0] = C(SymbolDictionary.Lith);   // a single blocker, tier 1
        s.Current.FrontRow[0] = C("Liphiom");                // tier 3, length 3 — big, but blocked
        Check("life is shielded while any card is on the front row",
            !g.Attack(TcgAction.Attack(0, TcgZone.Life, 0)) && s.Opponent.LifeRow[0] != null);
        s.Opponent.FrontRow[0] = null;                       // clear the board
        Check("with the front row empty, life is reachable",
            TcgRules.CanAttack(s, TcgAction.Attack(0, TcgZone.Life, 0)));

        // Tier gate applies to life too: a card that reaches by length but is too low in tier
        // cannot touch the life card.
        var gt = Rigged();
        gt.State.Opponent.LifeRow[0] = C("Laxkite");   // tier 4
        gt.State.Current.FrontRow[0] = C("Phiom");     // tier 3, front empty → reaches, but out-ranked
        Check("a life card outranks a lower attacker (no hit)",
            !gt.Attack(TcgAction.Attack(0, TcgZone.Life, 0)) && gt.State.Opponent.LifeRow[0] != null);

        // A higher attacker chips a life card one symbol at a time.
        var g2 = Rigged();
        g2.State.Opponent.LifeRow[0] = C("Laxkite");   // tier 4, length 3
        g2.State.Opponent.LifeRow[1] = C("Mophi");     // second life card → no win yet
        g2.State.Current.FrontRow[0] = C("Quilom");    // tier 5 → chips the peak
        Check("a higher card chips a life card by one symbol",
            g2.Attack(TcgAction.Attack(0, TcgZone.Life, 0)) &&
            g2.State.Opponent.LifeRow[0] != null && g2.State.Opponent.LifeRow[0].Length == 2 &&
            g2.State.Winner == -1);

        // A same-tier attacker deletes a whole life card at the cost of itself.
        var g3 = Rigged();
        g3.State.Opponent.LifeRow[0] = C("Mophi");     // tier 4, length 2
        g3.State.Opponent.LifeRow[1] = C("Liphi");     // second life card → no win yet
        g3.State.Current.FrontRow[0] = C("Laxkite");   // tier 4 → mutual trade
        Check("a same-tier trade deletes the whole life card and the attacker",
            g3.Attack(TcgAction.Attack(0, TcgZone.Life, 0)) &&
            g3.State.Opponent.LifeRow[0] == null && g3.State.Current.FrontRow[0] == null &&
            g3.State.Winner == -1);

        // Destroying the last life card wins — a mutual trade into the sole life card (Mophi).
        var g4 = Rigged();   // Rigged deals opponent a single life card: Mophi (front row empty)
        g4.State.Current.FrontRow[0] = C("Laxkite");   // tier 4 → mutual with Mophi (tier 4)
        Check("destroying the last life card wins",
            g4.Attack(TcgAction.Attack(0, TcgZone.Life, 0)) && g4.State.Winner == 0);
        Check("no actions after the game ends", !g4.EndTurn());
    }

    private static void TurnFlow()
    {
        Console.WriteLine("Turn flow");
        var g = new TcgGame(seed: 7);
        var s = g.State;
        int deckSize = SymbolDictionary.All.Count * TcgGameState.CopiesPerSymbol;
        Check("first player starts with 6 cards (5 + turn-1 draw)", s.Players[0].Hand.Count == TcgGameState.StartingHand + 1);
        Check("second player starts with 5", s.Players[1].Hand.Count == TcgGameState.StartingHand);
        Check("decks hold the remainder", s.Players[0].Deck.Count == deckSize - TcgGameState.StartingHand - 1 &&
                                          s.Players[1].Deck.Count == deckSize - TcgGameState.StartingHand);
        Check("each player has 3 distinct life-eligible words",
            s.Players.All(p => p.LifeRow.All(c => c != null && c.Word.CanBeLife) && p.LifeRow.Select(c => c.Word).Distinct().Count() == 3));

        int before = s.Players[1].Hand.Count;
        g.EndTurn();
        Check("EndTurn passes play and draws for the new player",
            s.CurrentPlayer == 1 && s.TurnNumber == 2 && s.Players[1].Hand.Count == before + 1);

        s.Current.Deck.Clear();
        g.EndTurn();
        int p0Hand = s.Players[0].Hand.Count;
        g.EndTurn();   // back to player 1, whose deck is empty
        Check("an empty deck simply skips the draw", s.CurrentPlayer == 1 && s.Players[0].Hand.Count == p0Hand);
    }

    // ── AI ─────────────────────────────────────────────────────────────────────

    private static void AiFullGames()
    {
        Console.WriteLine("AI vs AI full games (100 seeds)");
        int decisive = 0, drawn = 0, illegal = 0, runaway = 0;
        long totalTurns = 0;
        const int turnCap = 600;

        for (int seed = 0; seed < 100; seed++)
        {
            var g = new TcgGame(seed);
            int actions = 0;
            while (g.State.Winner < 0 && g.State.TurnNumber <= turnCap)
            {
                var a = TcgAi.NextAction(g.State);
                if (!TcgRules.IsLegal(g.State, a) || !g.Apply(a)) { illegal++; break; }
                if (++actions > 500_000) { runaway++; break; }   // a turn that never reaches EndTurn
            }
            if (g.State.Winner >= 0) { decisive++; totalTurns += g.State.TurnNumber; }
            else drawn++;
        }

        // Under the v3 attrition rules every attack removes a symbol, so mirror games are expected
        // to be fully decisive — asserted, not just reported.
        Check("every AI action proposed was legal", illegal == 0);
        Check("no game failed to terminate", runaway == 0);
        Check($"all 100 games ended with a winner (decisive {decisive}, drawn {drawn}" +
              (decisive > 0 ? $", avg {totalTurns / decisive} turns)" : ")"), decisive == 100);
    }

    private static void AiIgnoresEnemyHand()
    {
        Console.WriteLine("AI honesty");
        bool allSame = true;
        for (int seed = 0; seed < 20; seed++)
        {
            var a = new TcgGame(seed);
            var b = new TcgGame(seed);
            a.EndTurn();
            b.EndTurn();                          // both now on the AI player's turn
            b.State.Opponent.Hand.Reverse();      // scramble only the enemy hand
            var actA = TcgAi.NextAction(a.State);
            var actB = TcgAi.NextAction(b.State);
            if (Fmt(actA) != Fmt(actB)) allSame = false;
        }
        Check("decisions unchanged when the enemy hand is shuffled", allSame);
    }

    private static string Fmt(TcgAction a) => $"{a.Kind}:{a.SourceZone}{a.SourceIndex}->{a.TargetZone}{a.TargetIndex}";

    // ── Stall diagnostics (dotnet run -- diag) ─────────────────────────────────

    private static void DiagnoseStalls()
    {
        const int turnCap = 400;
        int shown = 0;
        for (int seed = 0; seed < 100 && shown < 5; seed++)
        {
            var g = new TcgGame(seed);
            int guard = 0;
            while (g.State.Winner < 0 && g.State.TurnNumber <= turnCap && guard++ < 100_000)
            {
                var a = TcgAi.NextAction(g.State);
                if (!g.Apply(a)) break;
            }
            if (g.State.Winner >= 0) continue;

            shown++;
            var s = g.State;
            Console.WriteLine($"── seed {seed} stalled at turn {s.TurnNumber} ──");
            for (int p = 0; p < 2; p++)
            {
                var pl = s.Players[p];
                Console.WriteLine($"  P{p}: deck {pl.Deck.Count}, lost {pl.CardsLost}");
                Console.WriteLine($"      hand  [{string.Join(", ", pl.Hand.Select(Desc))}]");
                Console.WriteLine($"      front [{string.Join(", ", pl.FrontRow.Select(Desc))}]");
                Console.WriteLine($"      life  [{string.Join(", ", pl.LifeRow.Select(Desc))}]");
            }
        }
        if (shown == 0) Console.WriteLine("No stalls in seeds 0-99.");
    }

    private static string Desc(TcgCard c) => c == null ? "-"
        : $"{c.DisplayName}(T{c.Tier}{WordDictionary.SideGlyph(c.Sidedness)}x{c.Length})";

    // ── First-player-advantage experiment (dotnet run -- winrate [games]) ──────
    // Both seats are played by the same AI, so any skew in P0's win rate is pure
    // turn-order advantage. P0 always goes first (and skips attacks on turn 1).

    private static void WinRate(int games)
    {
        int p0 = 0, p1 = 0, draws = 0;
        long p0Turns = 0, p1Turns = 0;
        int minTurns = int.MaxValue, maxTurns = 0;
        const int turnCap = 600;

        for (int seed = 0; seed < games; seed++)
        {
            var g = new TcgGame(seed);
            int guard = 0;
            while (g.State.Winner < 0 && g.State.TurnNumber <= turnCap && guard++ < 500_000)
                if (!g.Apply(TcgAi.NextAction(g.State))) break;

            int t = g.State.TurnNumber;
            if (g.State.Winner == 0) { p0++; p0Turns += t; }
            else if (g.State.Winner == 1) { p1++; p1Turns += t; }
            else { draws++; continue; }
            if (t < minTurns) minTurns = t;
            if (t > maxTurns) maxTurns = t;
        }

        int decided = p0 + p1;
        Console.WriteLine($"{games} mirror games (identical AI both seats):");
        Console.WriteLine($"  First player (P0):  {p0} wins  ({100.0 * p0 / decided:F1}% of decided games)");
        Console.WriteLine($"  Second player (P1): {p1} wins  ({100.0 * p1 / decided:F1}%)");
        if (draws > 0) Console.WriteLine($"  Draws/stalls: {draws}");
        if (p0 > 0) Console.WriteLine($"  Avg turns when P0 wins: {(double)p0Turns / p0:F1}");
        if (p1 > 0) Console.WriteLine($"  Avg turns when P1 wins: {(double)p1Turns / p1:F1}");
        Console.WriteLine($"  Game length range: {minTurns}-{maxTurns} turns");

        // Rough significance guide: under a fair coin, p0 should be within ~2*sqrt(games)/2 of half.
        double half = decided / 2.0, sigma = System.Math.Sqrt(decided) / 2.0;
        Console.WriteLine($"  (fair-coin expectation {half:F0} ± {2 * sigma:F0}; outside that band = real advantage)");
    }

    // ── Single-game action trace (dotnet run -- trace [seed]) ──────────────────

    private static void TraceGame(int seed)
    {
        var g = new TcgGame(seed);
        var s = g.State;
        int guard = 0;
        while (s.Winner < 0 && guard++ < 2000)
        {
            var a = TcgAi.NextAction(s);
            string desc = a.Kind switch
            {
                TcgActionKind.Summon => $"summon {Desc(s.Current.Hand[a.SourceIndex])} -> slot {a.TargetIndex}",
                TcgActionKind.Fuse => $"fuse {Desc(s.Current.FrontRow[a.SourceIndex])} + {Desc(s.Current.FrontRow[a.TargetIndex])}",
                TcgActionKind.Attack => $"attack {Desc(s.Current.FrontRow[a.SourceIndex])} -> {a.TargetZone} {Desc(TcgRules.GetEnemy(s, a.TargetZone, a.TargetIndex))}" +
                                        $" ({(TcgRules.IsMutual(s.Current.FrontRow[a.SourceIndex], TcgRules.GetEnemy(s, a.TargetZone, a.TargetIndex)) ? "mutual" : "chip")})",
                _ => "end turn",
            };
            Console.WriteLine($"T{s.TurnNumber} P{s.CurrentPlayer}: {desc}");
            if (!g.Apply(a)) { Console.WriteLine("  !! illegal"); break; }
        }
        Console.WriteLine($"Winner: P{s.Winner} on turn {s.TurnNumber}");
        Console.WriteLine($"P0 life: [{string.Join(", ", s.Players[0].LifeRow.Select(Desc))}]");
        Console.WriteLine($"P1 life: [{string.Join(", ", s.Players[1].LifeRow.Select(Desc))}]");
    }
}
