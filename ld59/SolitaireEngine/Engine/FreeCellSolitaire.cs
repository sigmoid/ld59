using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

public class FreeCellSolitaire : SolitaireGameMode
{
    private const float CardWidth = 100f;
    private const float ColumnGap = 10f;
    private const float SideMargin = 10f;

    private readonly int _columnCount;
    private readonly int _freeCellCount;

    private readonly List<SolitaireStack> _tableau     = new();
    private readonly List<SolitaireStack> _freeCells    = new();
    private readonly List<SolitaireStack> _foundations  = new();

    public FreeCellSolitaire(int columnCount = 8, int freeCellCount = 4)
    {
        _columnCount   = Math.Max(1, columnCount);
        _freeCellCount = Math.Max(0, freeCellCount);
    }

    public override string Name => "FreeCell";
    public override bool IsWon => _foundations.All(f => f.Cards.Count == 13);

    // Largest run that may be moved in one go: (free cells + 1) doubled for each empty column.
    public int MaxMovableCards()
    {
        int openCells   = _freeCells.Count(c => c.Cards.Count == 0);
        int emptyColumns = _tableau.Count(t => t.Cards.Count == 0);
        return (openCells + 1) * (1 << emptyColumns);
    }

    public override void Initialize(float contentWidth)
    {
        _allStacks.Clear();
        _tableau.Clear();
        _freeCells.Clear();
        _foundations.Clear();

        var deck = CreateShuffledDeck();

        // Center the tableau within the available width; the top row aligns to its columns.
        float step         = CardWidth + ColumnGap;
        float tableauWidth = _columnCount * step - ColumnGap;
        float startX       = MathF.Max(SideMargin, (contentWidth - tableauWidth) * 0.5f);

        // ── Top row: free cells over the left columns, foundations over the right ──
        for (int i = 0; i < _freeCellCount; i++)
        {
            var cell = new SolitaireStack
            {
                Position             = new Vector2(startX + i * step, 15),
                Layout               = new StackedLayout(),
                Rules                = new FreeCellCellRules(),
                ShowEmptyPlaceholder = true,
            };
            _freeCells.Add(cell);
            _allStacks.Add(cell);
        }

        float foundationStartX = startX + MathF.Max(0, _columnCount - 4) * step;
        for (int i = 0; i < 4; i++)
        {
            var foundation = new SolitaireStack
            {
                Position             = new Vector2(foundationStartX + i * step, 15),
                Layout               = new StackedLayout(),
                Rules                = new KlondikeFoundationRules(),
                ShowEmptyPlaceholder = true,
            };
            _foundations.Add(foundation);
            _allStacks.Add(foundation);
        }

        // ── Tableau columns, all cards dealt face up ────────────────────────────
        for (int col = 0; col < _columnCount; col++)
        {
            var column = new SolitaireStack
            {
                Position             = new Vector2(startX + col * step, 180),
                Layout               = new FannedLayout { FanOffset = 35f },
                Rules                = new FreeCellTableauRules(MaxMovableCards),
                ShowEmptyPlaceholder = true,
            };
            _tableau.Add(column);
            _allStacks.Add(column);
        }

        // Round-robin deal across the columns.
        int c = 0;
        while (deck.Count > 0)
        {
            var card = deck[^1]; deck.RemoveAt(deck.Count - 1);
            card.IsFaceUp = true;
            _tableau[c % _columnCount].Cards.Add(card);
            c++;
        }
    }

    private static List<SolitaireCardInstance> CreateShuffledDeck()
    {
        var deck = new List<SolitaireCardInstance>();
        foreach (SolitaireCardSuit suit in Enum.GetValues<SolitaireCardSuit>())
            foreach (SolitaireCardRank rank in Enum.GetValues<SolitaireCardRank>())
                deck.Add(new SolitaireCardInstance { CardData = new SolitaireCardData { Suit = suit, Rank = rank }, IsFaceUp = false });

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Next(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }
}
