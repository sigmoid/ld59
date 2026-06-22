using System;
using System.Collections.Generic;
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
/// Only scalar auto-properties are serialized by Quartz; <see cref="OutgoingNodeNames"/> is a
/// collection, so it rides in the component Data blob via Serialize/DeserializeData. References are
/// by entity Name because entity Guids are regenerated on every load.
/// </summary>
public class PowerNodeComponent : Component
{
    // ── Serialized authoring data ──────────────────────────────────────────

    /// <summary>A pre-filled rune set by the author (a symbol name, e.g. "Lith"). Empty = the player
    /// fills this node freely. A fixed rune is shown from the start and cannot be changed during play
    /// (it looks like an ordinary fill, just locked).</summary>
    public string FixedRune { get; set; } = string.Empty;

    /// <summary>Names of the entities this node connects to. Connections are undirected for coloring
    /// (an edge in either direction means the two nodes are adjacent and must differ).</summary>
    public List<string> OutgoingNodeNames = new();

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
        => OutgoingNodeNames.Count > 0 ? string.Join(",", OutgoingNodeNames) : string.Empty;

    public override void DeserializeData(string data)
    {
        OutgoingNodeNames.Clear();
        if (string.IsNullOrWhiteSpace(data)) return;

        OutgoingNodeNames.AddRange(
            data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
