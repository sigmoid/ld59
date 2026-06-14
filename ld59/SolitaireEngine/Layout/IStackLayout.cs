using System.Collections.Generic;
using Microsoft.Xna.Framework;

public interface IStackLayout
{
    Vector2 GetCardOffset(IReadOnlyList<SolitaireCardInstance> cards, int index);
}

public class StackedLayout : IStackLayout
{
    public Vector2 GetCardOffset(IReadOnlyList<SolitaireCardInstance> cards, int index) => Vector2.Zero;
}

public class FannedLayout : IStackLayout
{
    public float FanOffset { get; init; } = 40f;

    public Vector2 GetCardOffset(IReadOnlyList<SolitaireCardInstance> cards, int index)
        => new Vector2(0, index * FanOffset);
}

public class KlondikeTableauLayout : IStackLayout
{
    public float FaceDownOffset { get; init; } = 20f;
    public float FaceUpOffset   { get; init; } = 35f;

    public Vector2 GetCardOffset(IReadOnlyList<SolitaireCardInstance> cards, int index)
    {
        float y = 0;
        for (int i = 0; i < index; i++)
            y += cards[i].IsFaceUp ? FaceUpOffset : FaceDownOffset;
        return new Vector2(0, y);
    }
}
