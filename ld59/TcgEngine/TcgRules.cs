using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure legality and damage rules over TcgGameState (tcg.md "Ruleset v2 — attrition combat").
/// Combat is universal: any card may attack any enemy card, and the symbol rules decide how hard
/// the hit lands, never whether it is allowed. All mutation lives in TcgGame; the UI uses the
/// Legal* enumerators for target highlighting and the AI uses IsLegal as a safety net.
/// </summary>
public static class TcgRules
{
    /// <summary>Combat legality by tier: a card can only knock a symbol off a target it outranks or
    /// ties — you can never remove a symbol that is not below your own top tier. A strictly higher
    /// attacker chips the target's peak; an equal attacker may instead chew through the defender's
    /// lower-tier symbols, and only a true tie destroys both cards. Sidedness plays no part in combat.</summary>
    public static bool CanDamage(TcgCard attacker, TcgCard target)
    {
        if (attacker.Tier < target.Tier) return false;   // can't touch a higher-tier target
        if (attacker.Tier == target.Tier)
        {
            var defenderSymbols = target.Symbols;

            var lowestDefenderSymbol = defenderSymbols.Min(s => s.Tier);

            if (lowestDefenderSymbol < attacker.Tier) return true;
        }
        return true;
    }

    /// <summary>Equal tiers destroy each other only on a true tie: same tier and same stack size, with no
    /// lower-tier symbol available to peel off first.</summary>
    public static bool IsMutual(TcgCard attacker, TcgCard target) =>
        attacker.Tier == target.Tier && attacker.Length == target.Length &&
        target.Symbols.All(s => s.Tier == attacker.Tier);

    // Fusion sidedness rule (tcg.md): two cards may combine if they share a side, or either is
    // ambiguous (center). Center is the universal glue between the two schools.
    public static bool CanCombineSides(TcgCard a, TcgCard b) =>
        a.Sidedness == 0 || b.Sidedness == 0 || a.Sidedness == b.Sidedness;

    public static bool IsLegal(TcgGameState s, TcgAction a) => a.Kind switch
    {
        TcgActionKind.Summon => CanSummon(s, a.SourceIndex, a.TargetIndex),
        TcgActionKind.Fuse => CanFuse(s, a),
        TcgActionKind.Attack => CanAttack(s, a),
        TcgActionKind.EndTurn => s.Winner < 0,
        _ => false,
    };

    public static bool CanSummon(TcgGameState s, int handIndex, int frontSlot) =>
        s.Winner < 0 && s.Phase == TcgPhase.Main &&
        s.SummonsUsed < TcgGameState.SummonLimit &&
        handIndex >= 0 && handIndex < s.Current.Hand.Count &&
        frontSlot >= 0 && frontSlot < TcgGameState.FrontSlots &&
        s.Current.FrontRow[frontSlot] == null;

    /// <summary>Fusion legality (tcg.md v3): two distinct cards that are both already summoned
    /// (on the field), with compatible sidedness AND tiers exactly one apart — fusion climbs the
    /// pyramid step by step. Once per turn, independent of the summon budget. The result is the
    /// union of their symbols.</summary>
    public static bool CanFuse(TcgGameState s, TcgAction a)
    {
        if (s.Winner >= 0 || s.Phase != TcgPhase.Main || s.FusionUsed) return false;
        if (a.SourceZone != TcgZone.Front || a.TargetZone != TcgZone.Front) return false;   // summoned cards only
        if (a.SourceIndex == a.TargetIndex) return false;

        var first = GetOwn(s, TcgZone.Front, a.SourceIndex);
        var second = GetOwn(s, TcgZone.Front, a.TargetIndex);
        return first != null && second != null &&
               CanCombineSides(first, second) &&
               System.Math.Abs(first.Tier - second.Tier) == 1;
    }

    public static bool CanAttack(TcgGameState s, TcgAction a)
    {
        if (s.Winner >= 0 || s.TurnNumber == 1) return false;   // first player skips attacks on turn 1
        var attacker = GetOwn(s, TcgZone.Front, a.SourceIndex);
        if (attacker == null || attacker.HasAttackedThisTurn) return false;
        if (a.TargetZone == TcgZone.Hand) return false;
        if (a.TargetZone == TcgZone.Life && !CanReachLife(s)) return false;
        var target = GetEnemy(s, a.TargetZone, a.TargetIndex);
        return target != null && CanDamage(attacker, target);   // must outrank or tie the target's tier
    }

    /// <summary>The life row is reachable only when the defender's front row is completely empty —
    /// you must clear the board before you can strike at their life.</summary>
    public static bool CanReachLife(TcgGameState s) =>
        s.Opponent.FrontRow.All(c => c == null);

    public static TcgCard GetOwn(TcgGameState s, TcgZone zone, int index) => Get(s.Current, zone, index);
    public static TcgCard GetEnemy(TcgGameState s, TcgZone zone, int index) => Get(s.Opponent, zone, index);

    private static TcgCard Get(TcgPlayerState p, TcgZone zone, int index) => zone switch
    {
        TcgZone.Hand => index >= 0 && index < p.Hand.Count ? p.Hand[index] : null,
        TcgZone.Front => index >= 0 && index < p.FrontRow.Length ? p.FrontRow[index] : null,
        TcgZone.Life => index >= 0 && index < p.LifeRow.Length ? p.LifeRow[index] : null,
        _ => null,
    };

    // ── Enumerators for UI highlighting and the AI ─────────────────────────────

    public static IEnumerable<int> LegalSummonSlots(TcgGameState s, int handIndex)
    {
        for (int slot = 0; slot < TcgGameState.FrontSlots; slot++)
            if (CanSummon(s, handIndex, slot))
                yield return slot;
    }

    /// <summary>The own front-row cards the given front card can legally fuse with
    /// (the given card's slot receives the combined card).</summary>
    public static IEnumerable<int> LegalFusionPartners(TcgGameState s, int frontIndex)
    {
        for (int i = 0; i < TcgGameState.FrontSlots; i++)
            if (CanFuse(s, TcgAction.Fuse(TcgZone.Front, frontIndex, TcgZone.Front, i)))
                yield return i;
    }

    public static IEnumerable<(TcgZone Zone, int Index)> LegalAttackTargets(TcgGameState s, int attackerSlot)
    {
        for (int i = 0; i < s.Opponent.FrontRow.Length; i++)
            if (CanAttack(s, TcgAction.Attack(attackerSlot, TcgZone.Front, i)))
                yield return (TcgZone.Front, i);
        for (int i = 0; i < s.Opponent.LifeRow.Length; i++)
            if (CanAttack(s, TcgAction.Attack(attackerSlot, TcgZone.Life, i)))
                yield return (TcgZone.Life, i);
    }
}
