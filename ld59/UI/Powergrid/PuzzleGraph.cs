using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ld59.UI.Powergrid;

/// <summary>
/// Runtime model for one sub-puzzle of a graph-colouring level: a set of
/// <see cref="PowerNodeComponent"/>s joined by undirected <see cref="Connection"/>s. The player
/// fills each node with a rune so that no edge joins two nodes holding the same rune. Rebuilt each
/// session from the scene's components (never serialized).
///
/// <para>Win condition: every node holds a rune and no edge is in conflict.</para>
/// </summary>
public class PuzzleGraph
{
    private readonly List<PowerNodeComponent> _nodes;
    private readonly List<Connection> _connections = new();

    /// <summary>Endpoint inset from node centres, so lines visually meet the circle edges.</summary>
    private const float LinePadding = 0.4f;

    /// <summary>Sub-puzzle id (smallest node name in the component); set by the controller.</summary>
    public string Id { get; set; } = "default";

    /// <summary>Authored sequence position (set by the controller from the level config). Lower puzzles
    /// must be solved before this one unlocks; ties are broken by <see cref="Id"/>.</summary>
    public int Order { get; set; }

    /// <summary>Runes this puzzle grants (once, permanently) the first time it is solved. Set by the
    /// controller from the level config; the view consumes it.</summary>
    public IReadOnlyList<string> RewardRunes { get; set; } = System.Array.Empty<string>();

    /// <summary>True when every earlier puzzle in the sequence is currently solved, so the player may
    /// place/lift runes here. Recomputed by the controller each update; the first puzzle is always
    /// unlocked.</summary>
    public bool Unlocked { get; set; } = true;

    /// <summary>Active adjacency rules (set by the controller from the level config). An edge conflicts
    /// if its two runes violate any of these.</summary>
    public IReadOnlyList<ColoringRule> Rules { get; set; } = new[] { ColoringRule.DifferentRune };

    /// <summary>True once every node holds a rune and no edge is in conflict.</summary>
    public bool IsSolved { get; private set; }

    /// <summary>True while any edge joins two nodes holding the same rune.</summary>
    public bool HasConflict { get; private set; }

    public IReadOnlyList<Connection> Connections => _connections;
    public IReadOnlyList<PowerNodeComponent> Nodes => _nodes;

    /// <summary>Average of node positions — used for camera snap.</summary>
    public Vector2 Centroid
    {
        get
        {
            if (_nodes.Count == 0) return Vector2.Zero;
            var sum = Vector2.Zero;
            foreach (var n in _nodes) sum += n.Entity.Position;
            return sum / _nodes.Count;
        }
    }

    public PuzzleGraph(List<PowerNodeComponent> nodes, Func<string, PowerNodeComponent> resolve)
    {
        _nodes = nodes;
        BuildConnections(resolve);
        Recompute();
    }

    private static Vector2 Pos(PowerNodeComponent n) => n.Entity.Position;

    private void BuildConnections(Func<string, PowerNodeComponent> resolve)
    {
        foreach (var node in _nodes)
            foreach (var targetName in node.OutgoingNodeNames)
            {
                var target = resolve(targetName);
                if (target == null || target == node) continue;

                var a = Pos(node);
                var b = Pos(target);
                var dir = b - a;
                if (dir != Vector2.Zero) dir.Normalize();

                _connections.Add(new Connection
                {
                    From = node,
                    To = target,
                    StartPos = a + dir * LinePadding,
                    EndPos = b - dir * LinePadding,
                });
            }
    }

    /// <summary>Re-evaluates conflicts and the solved state from the nodes' current runes. Cheap;
    /// called every frame by the view.</summary>
    public void Recompute()
    {
        foreach (var n in _nodes) n.InConflict = false;

        bool anyConflict = false;
        foreach (var c in _connections)
        {
            bool clash = false;
            if (c.From.HasRune && c.To.HasRune)
            {
                var a = Runes.ByName(c.From.Rune);
                var b = Runes.ByName(c.To.Rune);
                if (a != null && b != null)
                    foreach (var rule in Rules)
                        if (ColoringRules.Violates(rule, a, b)) { clash = true; break; }
            }
            c.Conflict = clash;
            if (clash)
            {
                c.From.InConflict = true;
                c.To.InConflict = true;
                anyConflict = true;
            }
        }

        HasConflict = anyConflict;

        bool allFilled = true;
        foreach (var n in _nodes)
            if (!n.HasRune) { allFilled = false; break; }

        IsSolved = allFilled && !anyConflict && _nodes.Count > 0;
    }

    /// <summary>Clears every player-placed rune (fixed clues are kept).</summary>
    public void ClearPlaced()
    {
        foreach (var n in _nodes) n.PlacedRune = string.Empty;
        Recompute();
    }
}
