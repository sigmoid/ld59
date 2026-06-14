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

    public override void Initialize()
    {
        _allStacks.Clear();
        _foundations.Clear();

        var deck = CreateShuffledDeck();

        float[] tableauX = { 10, 120, 230, 340, 450, 560, 670 };
        for (int col = 0; col < 7; col++)
        {
            var stack = new SolitaireStack
            {
                Position             = new Vector2(tableauX[col], 180),
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
            Position             = new Vector2(10, 15),
            Layout               = new StackedLayout(),
            Rules                = new KlondikeStockRules(),
            ShowEmptyPlaceholder = true,
        };
        _stock.Cards.AddRange(deck);
        _allStacks.Add(_stock);

        _waste = new SolitaireStack
        {
            Position = new Vector2(120, 15),
            Layout   = new StackedLayout(),
            Rules    = new KlondikeWasteRules(),
        };
        _allStacks.Add(_waste);

        float[] foundationX = { 340, 450, 560, 670 };
        for (int i = 0; i < 4; i++)
        {
            var foundation = new SolitaireStack
            {
                Position             = new Vector2(foundationX[i], 15),
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
