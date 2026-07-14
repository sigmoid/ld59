using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Greedy one-ply opponent for the v3 attrition rules (tcg.md). One action per call, stateless,
/// reads only its own hand plus both public boards — never the enemy hand.
///
/// Each turn: summon the highest-tier body it can (bodies both clear enemy cards and shield its own
/// life row, since life is only reachable once the front row is empty), then attack — a life card
/// when the board is clear (mutual-trade to delete a whole card, or chip a lower-tier one for free),
/// otherwise the most valuable enemy front card. Fusion is left to the player: under the tier gate
/// a fused card's tier is only the higher of its parts, so combining never lets the AI outrank
/// anything it couldn't already, and it isn't worth the lost board width.
/// </summary>
public static class TcgAi
{
    public static TcgAction NextAction(TcgGameState s)
    {
        if (s.Winner >= 0) return TcgAction.EndTurn();
        var me = s.Current;

        if (s.Phase == TcgPhase.Main && s.SummonsUsed < TcgGameState.SummonLimit && me.Hand.Count > 0)
        {
            int slot = me.FirstFreeFrontSlot();
            if (slot >= 0)
            {
                int best = 0;
                for (int i = 1; i < me.Hand.Count; i++)
                    if (me.Hand[i].Symbols[0].Tier > me.Hand[best].Symbols[0].Tier) best = i;
                return TcgAction.Summon(best, slot);
            }
        }

        var attacks = AllAttacks(s).ToList();
        if (attacks.Count > 0)
            return attacks.OrderByDescending(t => Score(t.Zone, t.Attacker, t.Target)).First().Action;

        return TcgAction.EndTurn();
    }

    // Life pressure dominates. A same-tier attack deletes a whole life card only on a true tie;
    // otherwise an equal-tier hit can strip a lower-tier symbol for free. Against the board, chip
    // the biggest threat for free; spend a true equal-tier trade only when nothing better exists.
    private static int Score(TcgZone zone, TcgCard attacker, TcgCard target)
    {
        bool mutual = TcgRules.IsMutual(attacker, target);
        if (zone == TcgZone.Life)
            return mutual ? 1200 + target.Length * 10          // delete the whole card
                          : 1000 + (target.Length == 1 ? 100 : 0) - target.Length;   // chip it for free
        return mutual ? 40 + target.Length                     // trade (last resort)
                      : 300 + target.Length * 10;              // free chip on the longest threat
    }

    private static IEnumerable<(TcgAction Action, TcgZone Zone, TcgCard Attacker, TcgCard Target)> AllAttacks(TcgGameState s)
    {
        for (int slot = 0; slot < TcgGameState.FrontSlots; slot++)
            foreach (var (zone, index) in TcgRules.LegalAttackTargets(s, slot))
                yield return (TcgAction.Attack(slot, zone, index), zone,
                              s.Current.FrontRow[slot], TcgRules.GetEnemy(s, zone, index));
    }
}
