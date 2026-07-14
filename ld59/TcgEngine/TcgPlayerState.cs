using System.Collections.Generic;
using System.Linq;

/// <summary>One player's zones. Slot arrays use null for an empty slot.</summary>
public class TcgPlayerState
{
    public List<Symbol> Deck = new();
    public List<TcgCard> Hand = new();
    public TcgCard[] FrontRow = new TcgCard[TcgGameState.FrontSlots];
    public TcgCard[] LifeRow = new TcgCard[TcgGameState.LifeSlots];
    public int CardsLost;                // board cards destroyed in battle (UI counter)

    public bool HasLife => LifeRow.Any(c => c != null);
    public int FirstFreeFrontSlot()
    {
        for (int i = 0; i < FrontRow.Length; i++)
            if (FrontRow[i] == null) return i;
        return -1;
    }
}
