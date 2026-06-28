using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Components;

namespace ld59.UI.Powergrid;

/// <summary>
/// Data for a single node in a graph-coloring puzzle. Lives on an <see cref="Quartz.Entity"/>;
/// the entity's LocalPosition is the node's graph position.
///
/// The player fills nodes with <b>runes</b> such that no two connected nodes share a rune. A node
/// may carry a <see cref="FixedRune"/> — a pre-filled rune set by the author that's shown from the
/// start and can't be changed in play. It renders identically to a player-filled node; it's simply
/// locked. The player's own placement lives in the runtime <see cref="PlacedRune"/> field, which is
/// never serialized.
///
/// Only scalar auto-properties are serialized by Quartz; collections ride in the component Data blob
/// via Serialize/DeserializeData. References are by entity Name because entity Guids are regenerated
/// on every load.
///
/// Data blob format: comma-separated outgoing node entries. Each entry is either a plain name ("n1")
/// or a name with a per-connection rule override ("n1:Rune+Step"). The override uses the short names
/// from <see cref="ColoringRules.ShortName"/>. A null override means "inherit level rules".
/// </summary>
public class PowerNodeComponent : Component
{
    // ── Serialized authoring data ──────────────────────────────────────────

    /// <summary>A pre-filled rune set by the author (a symbol name, e.g. "Lith"). Empty = the player
    /// fills this node freely. A fixed rune is shown from the start and cannot be changed during play
    /// (it looks like an ordinary fill, just locked).</summary>
    public string FixedRune { get; set; } = string.Empty;

    /// <summary>How many hops this node's rune reaches when checking coloring rules. Default 1 = only
    /// direct neighbors. 2 = direct neighbors and all nodes exactly 2 real-edge hops away, etc.
    /// Nodes within the influence radius get a virtual connection (drawn as a dashed line). BFS
    /// traverses only real edges; the shortest path depth is used, so cycles are handled naturally.</summary>
    public int Influence { get; set; } = 1;

    /// <summary>Names of the entities this node connects to. Connections are undirected for coloring
    /// (an edge in either direction means the two nodes are adjacent and must differ).</summary>
    public List<string> OutgoingNodeNames = new();

    /// <summary>Per-connection rule overrides, keyed by target node name. When present, replaces the
    /// level-wide rules for that specific edge. Serialized as ":Rule1+Rule2" suffixes on the node name
    /// in the Data blob.</summary>
    public Dictionary<string, List<ColoringRule>> ConnectionRuleOverrides = new();

    // ── Runtime-only state (fields → never serialized) ─────────────────────

    /// <summary>The rune the player has placed on this node during play (a symbol name; empty = none).
    /// Ignored for nodes that have a <see cref="FixedRune"/>.</summary>
    public string PlacedRune = string.Empty;

    /// <summary>True while at least one incident edge is in conflict (same rune on both ends).
    /// Recomputed each frame by <see cref="PuzzleGraph"/>; drives node rendering.</summary>
    public bool InConflict;

    /// <summary>The effective rune on this node: the fixed clue if present, else the placed rune.</summary>
    public string Rune => string.IsNullOrEmpty(FixedRune) ? PlacedRune : FixedRune;

    public bool HasRune => !string.IsNullOrEmpty(Rune);

    /// <summary>A fixed-clue node can't be re-coloured by the player.</summary>
    public bool IsFixed => !string.IsNullOrEmpty(FixedRune);

    public override string SerializeData()
    {
        if (OutgoingNodeNames.Count == 0) return string.Empty;
        return string.Join(",", OutgoingNodeNames.Select(name =>
        {
            if (ConnectionRuleOverrides.TryGetValue(name, out var rules) && rules.Count > 0)
                return name + ":" + string.Join("+", rules.Select(ColoringRules.ShortName));
            return name;
        }));
    }

    public override void DeserializeData(string data)
    {
        OutgoingNodeNames.Clear();
        ConnectionRuleOverrides.Clear();
        if (string.IsNullOrWhiteSpace(data)) return;

        foreach (var part in data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon > 0)
            {
                var name = part[..colon].Trim();
                OutgoingNodeNames.Add(name);
                var rules = new List<ColoringRule>();
                foreach (var token in part[(colon + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (ColoringRules.TryParse(token, out var rule)) rules.Add(rule);
                if (rules.Count > 0) ConnectionRuleOverrides[name] = rules;
            }
            else
            {
                OutgoingNodeNames.Add(part);
            }
        }
    }
}
