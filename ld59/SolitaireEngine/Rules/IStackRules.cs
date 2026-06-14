using System.Collections.Generic;

public interface IStackRules
{
    bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex);
    bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming);
}

public class FreeStackRules : IStackRules
{
    public bool CanPickUp(IReadOnlyList<SolitaireCardInstance> stack, int fromIndex) => true;
    public bool CanAccept(IReadOnlyList<SolitaireCardInstance> stack, IReadOnlyList<SolitaireCardInstance> incoming) => true;
}
