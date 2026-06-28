using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ld59.UI.Powergrid;

/// <summary>
/// A runtime edge between two nodes. Rebuilt each session from the node components' outgoing
/// references — never serialized. For graph colouring the edge is undirected (it only asserts that
/// its two endpoints are adjacent and must hold different runes).
/// </summary>
public class Connection
{
    public PowerNodeComponent From;
    public PowerNodeComponent To;

    /// <summary>Padded endpoints (offset from node centres) so lines visually meet the circle edges.</summary>
    public Vector2 StartPos;
    public Vector2 EndPos;

    /// <summary>True when both endpoints hold the same rune (a colouring violation). Recomputed each
    /// frame by <see cref="PuzzleGraph"/>; drives the line colour.</summary>
    public bool Conflict;

    /// <summary>When non-null, these rules replace the level-wide rules for this specific edge. Null
    /// means "inherit from the level". Set by the author via the connection inspector and persisted in
    /// the owning node's <see cref="PowerNodeComponent.ConnectionRuleOverrides"/>.</summary>
    public List<ColoringRule> RuleOverride;

    /// <summary>True for connections synthesised from a node's <see cref="PowerNodeComponent.Influence"/>
    /// radius rather than an authored edge. Virtual connections are never serialized, never selectable,
    /// and are recomputed each time the graph is built.</summary>
    public bool IsVirtual;
}
