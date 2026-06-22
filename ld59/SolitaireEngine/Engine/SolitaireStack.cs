using System.Collections.Generic;
using Microsoft.Xna.Framework;

public class SolitaireStack
{
    public List<SolitaireCardInstance> Cards { get; } = new();
    public Vector2 Position { get; set; }
    public IStackLayout Layout { get; set; }
    public IStackRules Rules { get; set; }
    public bool ShowEmptyPlaceholder { get; set; }

    // Set when a completed stack has been removed from play; the slot becomes inert and shows a card back.
    public bool IsCompleted { get; set; }
}
