using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Components;

namespace ld59.UI.Powergrid;

/// <summary>
/// Level-wide configuration, authored on a single config entity. Holds the player's <em>starting</em>
/// rune <see cref="Inventory"/> (a finite multiset — any number of any rune), the active adjacency
/// <see cref="ColoringRule"/>s, and per-puzzle sequence metadata: each sub-puzzle's authored
/// <see cref="PuzzleOrder"/> (lower = earlier in the sequence) and the reward runes it grants when
/// first solved (<see cref="PuzzleRewards"/>). Puzzles are keyed by their id (the smallest node name
/// in the component). Stored in the component Data blob.
///
/// Data format: ';'-separated "key:value" —
///   "inventory:Lith,Lith,Axe;rules:DifferentRune,Sidedness;order:n0=0|n3=1;rewards:n0=Lith,Axe".
/// Within order/rewards, puzzles are '|'-separated "id=value"; a reward value is a ','-separated
/// flat rune list (repeats = quantity).
/// </summary>
public class PowergridLevelComponent : Component
{
    /// <summary>The runes the player starts with, as a flat list (repeats = quantity).</summary>
    public List<string> Inventory = new();

    /// <summary>Active adjacency rules. Defaults to the base "different rune" constraint.</summary>
    public List<ColoringRule> Rules = new() { ColoringRule.DifferentRune };

    /// <summary>Authored sequence position per puzzle id (lower solves first). Absent ⇒ 0.</summary>
    public Dictionary<string, int> PuzzleOrder = new();

    /// <summary>Reward runes granted (once, permanently) when a puzzle is first solved, per puzzle id.</summary>
    public Dictionary<string, List<string>> PuzzleRewards = new();

    public int OrderOf(string puzzleId)
        => puzzleId != null && PuzzleOrder.TryGetValue(puzzleId, out var o) ? o : 0;

    public IReadOnlyList<string> RewardOf(string puzzleId)
        => puzzleId != null && PuzzleRewards.TryGetValue(puzzleId, out var r) ? r : Array.Empty<string>();

    public override string SerializeData()
    {
        var parts = new List<string>();
        if (Inventory.Count > 0) parts.Add("inventory:" + string.Join(",", Inventory));
        if (Rules.Count > 0) parts.Add("rules:" + string.Join(",", Rules));
        if (PuzzleOrder.Count > 0)
            parts.Add("order:" + string.Join("|", PuzzleOrder.Select(kv => $"{kv.Key}={kv.Value}")));
        var rewards = PuzzleRewards.Where(kv => kv.Value.Count > 0).ToList();
        if (rewards.Count > 0)
            parts.Add("rewards:" + string.Join("|", rewards.Select(kv => $"{kv.Key}={string.Join(",", kv.Value)}")));
        return string.Join(";", parts);
    }

    public override void DeserializeData(string data)
    {
        Inventory = new List<string>();
        Rules = new List<ColoringRule>();
        PuzzleOrder = new Dictionary<string, int>();
        PuzzleRewards = new Dictionary<string, List<string>>();
        if (string.IsNullOrWhiteSpace(data))
        {
            Rules.Add(ColoringRule.DifferentRune);
            return;
        }

        foreach (var entry in data.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':');
            var key = colon >= 0 ? entry[..colon].Trim() : entry.Trim();
            var value = colon >= 0 ? entry[(colon + 1)..].Trim() : entry.Trim();

            switch (key)
            {
                case "inventory":
                    foreach (var r in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (Runes.ByName(r) != null) Inventory.Add(r);
                    break;

                case "rules":
                    foreach (var r in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (ColoringRules.TryParse(r, out var rule) && !Rules.Contains(rule))
                            Rules.Add(rule);
                    break;

                case "order":
                    foreach (var pair in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        var id = pair[..eq].Trim();
                        if (int.TryParse(pair[(eq + 1)..].Trim(), out var ord)) PuzzleOrder[id] = ord;
                    }
                    break;

                case "rewards":
                    foreach (var pair in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        var id = pair[..eq].Trim();
                        var list = new List<string>();
                        foreach (var r in pair[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (Runes.ByName(r) != null) list.Add(r);
                        if (list.Count > 0) PuzzleRewards[id] = list;
                    }
                    break;
            }
        }

        if (Rules.Count == 0) Rules.Add(ColoringRule.DifferentRune);
    }

    /// <summary>Distinct runes in the inventory, ordered by the alphabet (for stable UI).</summary>
    public IEnumerable<string> DistinctRunes()
        => SymbolDictionary.All.Select(s => s.Name).Where(Inventory.Contains);

    public int CountOf(string rune) => Inventory.Count(r => r == rune);
}
