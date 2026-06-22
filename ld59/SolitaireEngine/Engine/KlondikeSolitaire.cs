using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

public class KlondikeSolitaire : SolitaireGameMode
{
    private SolitaireStack _stock;
    private SolitaireStack _waste;
    private readonly List<SolitaireStack> _foundations = new();

    public override string Name => "Klondike";
    public override SolitaireStack StockStack => _stock;
    public override bool IsWon => _foundations.All(f => f.Cards.Count == 13);

    private const float CardWidth  = 100f;
    private const float ColumnGap  = 10f;
    private const float SideMargin = 10f;

    public override void Initialize(float contentWidth)
    {
        _allStacks.Clear();
        _foundations.Clear();

        var deck = CreateShuffledDeck();

        // Center the 7 tableau columns within the available width; the top row aligns to them.
        const int columns  = 7;
        float step         = CardWidth + ColumnGap;
        float tableauWidth = columns * step - ColumnGap;
        float startX       = MathF.Max(SideMargin, (contentWidth - tableauWidth) * 0.5f);

        for (int col = 0; col < columns; col++)
        {
            var stack = new SolitaireStack
            {
                Position             = new Vector2(startX + col * step, 180),
                Layout               = new KlondikeTableauLayout(),
                Rules                = new KlondikeTableauRules(),
                ShowEmptyPlaceholder = true,
            };
            for (int row = 0; row <= col; row++)
            {
                var card = deck[^1]; deck.RemoveAt(deck.Count - 1);
                card.IsFaceUp = (row == col);
                stack.Cards.Add(card);
            }
            _allStacks.Add(stack);
        }

        _stock = new SolitaireStack
        {
            Position             = new Vector2(startX, 15),
            Layout               = new StackedLayout(),
            Rules                = new KlondikeStockRules(),
            ShowEmptyPlaceholder = true,
        };
        _stock.Cards.AddRange(deck);
        _allStacks.Add(_stock);

        _waste = new SolitaireStack
        {
            Position = new Vector2(startX + step, 15),
            Layout   = new StackedLayout(),
            Rules    = new KlondikeWasteRules(),
        };
        _allStacks.Add(_waste);

        // Foundations sit over the rightmost four columns.
        float foundationStartX = startX + 3 * step;
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
    }

    public override void OnStockClicked()
    {
        if (_stock.Cards.Count > 0)
        {
            var card = _stock.Cards[^1];
            _stock.Cards.RemoveAt(_stock.Cards.Count - 1);
            card.IsFaceUp = true;
            _waste.Cards.Add(card);
        }
        else if (_waste.Cards.Count > 0)
        {
            for (int i = _waste.Cards.Count - 1; i >= 0; i--)
            {
                var card = _waste.Cards[i];
                card.IsFaceUp = false;
                _stock.Cards.Add(card);
            }
            _waste.Cards.Clear();
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
