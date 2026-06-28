using System;
using System.Collections.Generic;
using System.Linq;
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
        BuildVirtualConnections();
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

                // Prefer override stored on the "from" side; fall back to the "to" side.
                List<ColoringRule> ruleOverride = null;
                if (node.ConnectionRuleOverrides.TryGetValue(targetName, out var ro1))
                    ruleOverride = ro1;
                else if (target.ConnectionRuleOverrides.TryGetValue(node.Entity.Name, out var ro2))
                    ruleOverride = ro2;

                _connections.Add(new Connection
                {
                    From = node,
                    To = target,
                    StartPos = a + dir * LinePadding,
                    EndPos = b - dir * LinePadding,
                    RuleOverride = ruleOverride,
                });
            }
    }

    /// <summary>
    /// For every node whose <see cref="PowerNodeComponent.Influence"/> is greater than 1, does a BFS
    /// over real edges up to that depth and adds a virtual <see cref="Connection"/> to each newly
    /// reached node. Virtual connections are treated identically to real ones during validation and by
    /// the solver, but are drawn differently and cannot be selected or deleted in the editor.
    ///
    /// BFS uses real edges only (influence does not chain through other nodes' influence). Because BFS
    /// always finds the shortest path first, each reachable node is visited exactly once at its minimum
    /// depth, so cycles are handled naturally: a node already reached at depth 1 (a direct neighbor)
    /// is never added again at depth 2.
    /// </summary>
    private void BuildVirtualConnections()
    {
        // Only nodes with Influence > 1 need processing.
        if (_nodes.All(n => n.Influence <= 1)) return;

        // Real adjacency map (used for BFS traversal, built from the real connections).
        var realAdj = new Dictionary<PowerNodeComponent, List<PowerNodeComponent>>();
        foreach (var n in _nodes) realAdj[n] = new List<PowerNodeComponent>();
        foreach (var c in _connections) // only real connections exist at this point
        {
            realAdj[c.From].Add(c.To);
            realAdj[c.To].Add(c.From);
        }

        // Track every pair that already has a connection (real or virtual, either direction).
        var existing = new HashSet<(PowerNodeComponent, PowerNodeComponent)>();
        foreach (var c in _connections)
        {
            existing.Add((c.From, c.To));
            existing.Add((c.To, c.From));
        }

        foreach (var source in _nodes)
        {
            if (source.Influence <= 1) continue;

            // BFS from source; record each reached node's shortest-path depth.
            var depth = new Dictionary<PowerNodeComponent, int> { [source] = 0 };
            var queue = new Queue<PowerNodeComponent>();
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int d = depth[cur];
                if (d >= source.Influence) continue;

                foreach (var nb in realAdj[cur])
                {
                    if (!depth.ContainsKey(nb))
                    {
                        depth[nb] = d + 1;
                        queue.Enqueue(nb);
                    }
                }
            }

            // Add a virtual connection to every node reached beyond depth 1 that isn't already
            // connected by a real or virtual edge from this source.
            foreach (var (reached, d) in depth)
            {
                if (d <= 1 || reached == source) continue; // depth 1 = real edge already handles it
                if (existing.Contains((source, reached))) continue;

                var a = Pos(source);
                var b = Pos(reached);
                var dir = b - a;
                if (dir != Vector2.Zero) dir.Normalize();

                _connections.Add(new Connection
                {
                    From = source,
                    To = reached,
                    StartPos = a + dir * LinePadding,
                    EndPos = b - dir * LinePadding,
                    IsVirtual = true,
                    RuleOverride = null, // virtual edges always use level-wide rules
                });
                existing.Add((source, reached));
                existing.Add((reached, source)); // prevents a symmetric duplicate if reached also has influence
            }
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
                {
                    var effectiveRules = c.RuleOverride ?? Rules;
                    foreach (var rule in effectiveRules)
                        if (ColoringRules.Violates(rule, a, b)) { clash = true; break; }
                }
            }
            c.Conflict = clash;
            if (clash)
            {
                c.From.InConflict = true;
                c.To.InConflict   = true;
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
