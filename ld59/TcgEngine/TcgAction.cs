public enum TcgActionKind { Summon, Fuse, Attack, EndTurn }

public enum TcgZone { Hand, Front, Life }

/// <summary>
/// One atomic move, the shared vocabulary between the AI and the UI's selection state machine.
/// Summon: Source = hand index, Target = front slot. Fuse: Source = first-selected participant
/// (its slot receives the word if it's on the field), Target = second. Attack: Source = own
/// front slot, Target = enemy front/life slot.
/// </summary>
public class TcgAction
{
    public TcgActionKind Kind;
    public TcgZone SourceZone;
    public int SourceIndex;
    public TcgZone TargetZone;
    public int TargetIndex;

    public static TcgAction Summon(int handIndex, int frontSlot) => new()
    { Kind = TcgActionKind.Summon, SourceZone = TcgZone.Hand, SourceIndex = handIndex, TargetZone = TcgZone.Front, TargetIndex = frontSlot };

    public static TcgAction Fuse(TcgZone firstZone, int firstIndex, TcgZone secondZone, int secondIndex) => new()
    { Kind = TcgActionKind.Fuse, SourceZone = firstZone, SourceIndex = firstIndex, TargetZone = secondZone, TargetIndex = secondIndex };

    public static TcgAction Attack(int attackerSlot, TcgZone targetZone, int targetIndex) => new()
    { Kind = TcgActionKind.Attack, SourceZone = TcgZone.Front, SourceIndex = attackerSlot, TargetZone = targetZone, TargetIndex = targetIndex };

    public static TcgAction EndTurn() => new() { Kind = TcgActionKind.EndTurn };
}
