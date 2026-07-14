using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

// Experimental mode using the alien alphabet symbols. Tableau columns + free cells, with
// an equal-tier deck so all cards can be built into complete runs. No win condition yet.
public class SymbolsSolitaire : SolitaireGameMode
{
    private const float CardWidth   = 100f;
    private const float ColumnGap   = 10f;
    private const float SideMargin  = 10f;
    private const float FreeCellRowY = 15f;
    private const int   MaxFreeCells = 4;   // hard cap on total free cells, including run-completion rewards

    // X of the next free cell to add; advances as completing a run awards an extra free cell.
    private float _nextFreeCellX;

    private readonly int _columnCount;
    private readonly int _freeCellCount;
    private readonly int _copiesPerTierPerSuit;

    private readonly List<SolitaireStack> _tableau   = new();
    private readonly List<SolitaireStack> _freeCells = new();

    // copiesPerTierPerSuit gives every tier the same number of cards (equal tiers), so the
    // whole deck packs into complete runs. The deck is 5 tiers * 2 suits * copies cards,
    // forming 2*copies complete tier-5..1 runs.
    public SymbolsSolitaire(int columnCount = 8, int freeCellCount = 5, int copiesPerTierPerSuit = 3)
    {
        _columnCount          = Math.Max(1, columnCount);
        _freeCellCount        = Math.Max(0, freeCellCount);
        _copiesPerTierPerSuit = Math.Max(1, copiesPerTierPerSuit);
    }

    public override string Name => "Symbols";

    private static int MaxTier => SymbolDictionary.All.Max(s => s.Tier);

    // Won once every card is packed into complete runs: each tableau column is either a completed
    // (removed) run or empty, no cards remain in free cells, and at least one run was completed. Empty
    // columns must count as won — the deck forms fewer complete runs than there are columns, so a
    // solved board always leaves one or more columns empty.
    public override bool IsWon =>
        _tableau.Any(t => t.IsCompleted)
        && _tableau.All(t => t.IsCompleted || t.Cards.Count == 0)
        && _freeCells.All(c => c.Cards.Count == 0);

    // A column is complete when it is a full descending tier-max..1 run with alternating suits.
    public override bool IsStackComplete(SolitaireStack stack)
    {
        var cards = stack.Cards;
        int maxTier = MaxTier;
        if (cards.Count != maxTier) return false;
        if (cards[0].CardData.Symbol == null || cards[0].CardData.Symbol.Tier != maxTier) return false;
        for (int i = 0; i < cards.Count - 1; i++)
            if (!SymbolStackRule.IsRunStep(cards[i].CardData.Symbol.Tier,     (int)cards[i].CardData.SymbolSuit,
                                           cards[i + 1].CardData.Symbol.Tier, (int)cards[i + 1].CardData.SymbolSuit))
                return false;
        return true;
    }

    // Completing a run frees up the board, so reward it with an extra free cell — up to a hard cap of
    // MaxFreeCells total. The added cell is picked up automatically by BuildSolverProblem (which reads
    // _freeCells.Count as capacity).
    public override void OnStackCompleted(SolitaireStack stack)
    {
        if (_freeCells.Count >= MaxFreeCells) return;

        AddFreeCell(_nextFreeCellX);
        _nextFreeCellX += CardWidth + ColumnGap;
    }

    // Creates an empty free cell at x on the free-cell row and registers it for rendering/interaction.
    private void AddFreeCell(float x)
    {
        var cell = new SolitaireStack
        {
            Position             = new Vector2(x, FreeCellRowY),
            Layout               = new StackedLayout(),
            Rules                = new FreeCellCellRules(),
            ShowEmptyPlaceholder = true,
        };
        _freeCells.Add(cell);
        _allStacks.Add(cell);
    }

    // Snapshot the current layout (copied, so it's safe to hand to a background solver thread).
    // Completed columns are removed from play, so they are excluded — the solver only sees the
    // columns still in use, which keeps its winnability verdict honest.
    public SymbolsSolver.Problem BuildSolverProblem()
    {
        var active = _tableau.Where(t => !t.IsCompleted).ToList();
        var problem = new SymbolsSolver.Problem
        {
            ColumnCount      = active.Count,
            FreeCellCapacity = _freeCells.Count,
            MaxTier          = MaxTier,
        };

        foreach (var column in active)
            problem.Columns.Add(column.Cards.Select(Encode).ToList());

        foreach (var cell in _freeCells)
            if (cell.Cards.Count > 0)
                problem.FreeCells.Add(Encode(cell.Cards[^1]));

        return problem;
    }

    // Non-completed tableau columns, in the same order BuildSolverProblem uses, so a solver Move's
    // column index maps directly onto these.
    public IReadOnlyList<SolitaireStack> ActiveColumns => _tableau.Where(t => !t.IsCompleted).ToList();
    public IReadOnlyList<SolitaireStack> FreeCells => _freeCells;

    // Pack tier, sidedness and suit into one byte so the solver models the same stacking rules as the
    // live game: bit 0 = suit (Light/Dark), bits 1-2 = side (HorizontalSide -1/0/+1 stored as 0/1/2),
    // bits 3+ = tier.
    public static byte Encode(SolitaireCardInstance card)
    {
        var symbol = card.CardData.Symbol;
        int side   = SymbolDictionary.HorizontalSide(symbol) + 1;   // -1/0/+1 -> 0/1/2
        return (byte)((symbol.Tier << 3) | (side << 1) | (int)card.CardData.SymbolSuit);
    }

    public override void Initialize(float contentWidth)
    {
        _allStacks.Clear();
        _tableau.Clear();
        _freeCells.Clear();

        var deck = CreateShuffledDeck();

        // Center the tableau within the available width; free cells sit over the left columns.
        float step         = CardWidth + ColumnGap;
        float tableauWidth  = _columnCount * step - ColumnGap;
        float startX        = MathF.Max(SideMargin, (contentWidth - tableauWidth) * 0.5f);

        for (int i = 0; i < _freeCellCount; i++)
            AddFreeCell(startX + i * step);

        // Any free cell awarded later (by completing a run) is appended to the right of the last one.
        _nextFreeCellX = startX + _freeCellCount * step;

        for (int col = 0; col < _columnCount; col++)
        {
            var column = new SolitaireStack
            {
                Position             = new Vector2(startX + col * step, 180),
                Layout               = new FannedLayout { FanOffset = 35f },
                Rules                = new SymbolTableauRules(),
                ShowEmptyPlaceholder = true,
            };
            _tableau.Add(column);
            _allStacks.Add(column);
        }

        // Round-robin deal, all face up.
        int c = 0;
        while (deck.Count > 0)
        {
            var card = deck[^1]; deck.RemoveAt(deck.Count - 1);
            card.IsFaceUp = true;
            _tableau[c % _columnCount].Cards.Add(card);
            c++;
        }
    }

    private List<SolitaireCardInstance> CreateShuffledDeck()
    {
        var deck = BuildDeckComposition(_copiesPerTierPerSuit)
            .Select(e => new SolitaireCardInstance { CardData = new SolitaireCardData { Symbol = e.symbol, SymbolSuit = e.suit }, IsFaceUp = false })
            .ToList();

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Next(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }

    // Equal tiers: every tier contributes the same number of cards per suit, cycling through
    // that tier's symbols so each one still shows up. This makes the deck a whole number of
    // complete runs. Shared by the live deal and the solver's random-deal generator.
    private static List<(Symbol symbol, SymbolSuit suit)> BuildDeckComposition(int copiesPerTierPerSuit)
    {
        var deck = new List<(Symbol, SymbolSuit)>();
        foreach (var tier in SymbolDictionary.All.GroupBy(s => s.Tier).OrderBy(g => g.Key))
        {
            var symbols = tier.ToList();
            int total = copiesPerTierPerSuit * 2;  // half light, half dark
            for (int k = 0; k < total; k++)
            {
                var symbol = symbols[k % symbols.Count];
                var suit   = k < copiesPerTierPerSuit ? SymbolSuit.Light : SymbolSuit.Dark;
                deck.Add((symbol, suit));
            }
        }
        return deck;
    }
}
