using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace ld59.UI.Powergrid;

/// <summary>
/// Runtime model + solver for one sub-puzzle: a directed graph of <see cref="PowerNodeComponent"/>s
/// connected by <see cref="Connection"/>s, with optional <see cref="EdgeLockComponent"/>s. Rebuilt
/// each session from the scene's components (never serialized).
///
/// Ported from the original jam game's LevelManager, with cleanups:
///  - Distribution flows only along connection segments (the original's second, unconditional loop
///    over raw outgoing connections bypassed locks/crossings — dropped here so they actually gate flow).
///  - A fixpoint loop replaces the original's frame-to-frame event nudging, so satisfying a gate or
///    unlocking a lock admits power within the same Update().
///  - "Solved" means the goal node is *powered* (power reached it), matching the design doc, rather
///    than the original's "goal holds a token".
/// </summary>
public class PuzzleGraph
{
    private readonly List<PowerNodeComponent> _nodes;
    private readonly List<EdgeLockComponent> _locks;
    private readonly Dictionary<PowerNodeComponent, List<Connection>> _lines = new();
    private readonly List<Connection> _allConnections = new();

    /// <summary>Endpoint inset from node centres, so lines visually meet circle edges and crossing
    /// tests use edge points. Matches the original LinePadding (~ node radius).</summary>
    private const float LinePadding = 0.4f;
    private const int MaxFixpointIterations = 16;

    public bool IsActive = true;
    public bool IsSolved { get; private set; }

    /// <summary>Sub-puzzle id (from the entity tag); used for activation chaining.</summary>
    public string Id { get; set; } = "default";

    /// <summary>Average of node positions — used for camera snap and the inactive mask.</summary>
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

    public IReadOnlyList<Connection> Connections => _allConnections;
    public IReadOnlyList<PowerNodeComponent> Nodes => _nodes;
    public IReadOnlyList<EdgeLockComponent> Locks => _locks;

    public PuzzleGraph(
        List<PowerNodeComponent> nodes,
        List<EdgeLockComponent> locks,
        Func<string, PowerNodeComponent> resolve)
    {
        _nodes = nodes;
        _locks = locks ?? new List<EdgeLockComponent>();

        BuildConnections(resolve);
        PopulateCompetingConnections();
        PopulateLocks();
    }

    private static Vector2 Pos(PowerNodeComponent n) => n.Entity.Position;

    #region Build

    private void BuildConnections(Func<string, PowerNodeComponent> resolve)
    {
        foreach (var node in _nodes)
            _lines[node] = new List<Connection>();

        foreach (var node in _nodes)
        {
            foreach (var targetName in node.OutgoingNodeNames)
            {
                var target = resolve(targetName);
                if (target == null || target == node) continue;

                var conn = CreateConnection(node, target);
                _lines[node].Add(conn);
                _allConnections.Add(conn);
            }
        }
    }

    private static Connection CreateConnection(PowerNodeComponent from, PowerNodeComponent to)
    {
        var a = Pos(from);
        var b = Pos(to);
        var dir = b - a;
        if (dir != Vector2.Zero) dir.Normalize();

        return new Connection
        {
            From = from,
            To = to,
            StartPos = a + dir * LinePadding,
            EndPos = b - dir * LinePadding,
        };
    }

    private void PopulateCompetingConnections()
    {
        for (int i = 0; i < _allConnections.Count - 1; i++)
        {
            for (int j = i + 1; j < _allConnections.Count; j++)
            {
                var ci = _allConnections[i];
                var cj = _allConnections[j];

                // Edges meeting at a shared node are not "crossing".
                if (SharesEndpoint(ci, cj)) continue;

                if (Geometry.DoLinesIntersect(ci.StartPos, ci.EndPos, cj.StartPos, cj.EndPos))
                {
                    (ci.CompetingConnections ??= new List<Connection>()).Add(cj);
                    (cj.CompetingConnections ??= new List<Connection>()).Add(ci);
                }
            }
        }
    }

    private static bool SharesEndpoint(Connection a, Connection b)
        => a.From == b.From || a.From == b.To || a.To == b.From || a.To == b.To;

    private void PopulateLocks()
    {
        foreach (var conn in _allConnections)
        {
            foreach (var lck in _locks)
            {
                if (Geometry.DoLinesIntersect(conn.StartPos, conn.EndPos, lck.PointA, lck.PointB))
                    (conn.Locks ??= new List<EdgeLockComponent>()).Add(lck);
            }
        }
    }

    #endregion

    #region Power distribution

    public void Update()
    {
        ResetState();

        HashSet<PowerNodeComponent> lastSignature = null;
        for (int iter = 0; iter < MaxFixpointIterations; iter++)
        {
            UpdateLockStates();

            // Snapshot source power *before* clearing, so gates evaluate against the previous pass.
            var srcPower = _nodes.ToDictionary(n => n, GetPower);

            foreach (var n in _nodes)
            {
                n.PoweredFrom.Clear();
                n.IsActive = false;
            }
            foreach (var c in _allConnections)
                c.IsActive = false;

            foreach (var n in _nodes)
            {
                if (srcPower[n] > 0)
                {
                    n.IsActive = true;
                    Distribute(n, srcPower[n], new HashSet<PowerNodeComponent>());
                }
            }

            var signature = _nodes.Where(n => n.IsActive).ToHashSet();
            if (lastSignature != null && signature.SetEquals(lastSignature)) break;
            lastSignature = signature;
        }

        var goals = _nodes.Where(n => n.IsGoal).ToList();
        IsSolved = goals.Count > 0 && goals.All(g => g.IsActive);

        RevealPass();
    }

    /// <summary>
    /// Discovery: anchors and goals are always visible (start/end reference). A powered node reveals
    /// the nodes its connections point at (the frontier) — even through locks, matching the original,
    /// so the player can see where to push next. Discovery is sticky for the session.
    /// </summary>
    private void RevealPass()
    {
        foreach (var n in _nodes)
            if (n.IsAnchor || n.IsGoal)
                n.Discovered = true;

        foreach (var n in _nodes)
        {
            if (!n.IsActive) continue;
            if (_lines.TryGetValue(n, out var lines))
                foreach (var line in lines)
                    line.To.Discovered = true;
        }
    }

    private void ResetState()
    {
        foreach (var n in _nodes)
        {
            n.PoweredFrom.Clear();
            n.IsActive = false;
        }
        foreach (var c in _allConnections)
            c.IsActive = false;
        foreach (var l in _locks)
            l.IsLocked = true;
    }

    private void UpdateLockStates()
    {
        foreach (var l in _locks)
            l.IsLocked = l.KeyNodeRef == null || !l.KeyNodeRef.IsActive;
    }

    private void Distribute(PowerNodeComponent node, int power, HashSet<PowerNodeComponent> seen)
    {
        if (power <= 0 || !seen.Add(node)) return;
        if (!_lines.TryGetValue(node, out var lines)) return;

        foreach (var line in lines)
        {
            if (HasActiveLock(line) || HasCrossingConflict(line)) continue;

            line.IsActive = true;
            line.To.IsActive = true;
            line.To.PoweredFrom.Add(node);
            Distribute(line.To, power - 1, seen);
        }
    }

    private static bool HasActiveLock(Connection line)
        => line.Locks != null && line.Locks.Any(l => l.IsLocked);

    private static bool HasCrossingConflict(Connection line)
    {
        if (line.CompetingConnections == null) return false;

        foreach (var comp in line.CompetingConnections)
        {
            if (!comp.IsActive) continue;
            // A competitor that is itself locked can't carry power, so it isn't a real conflict.
            if (comp.Locks != null && comp.Locks.Any(l => l.IsLocked)) continue;
            return true;
        }
        return false;
    }

    /// <summary>Power a node supplies as a source this pass (dynamic; reads current PoweredFrom for gates).</summary>
    private int GetPower(PowerNodeComponent n)
    {
        if (n.Removed) return 0;

        return n.NodeKind switch
        {
            NodeKind.And => n.PoweredFrom.Count >= 2 ? 1 : 0,
            NodeKind.Xor => CanAnchorExactlyOnce(n) ? 1 : 0,
            _ => n.PlacedTokenPower,
        };
    }

    private int ConvertPower(PowerNodeComponent n, int inputPower)
        => n.NodeKind switch
        {
            NodeKind.And => n.PoweredFrom.Count >= 2 ? 1 : 0,
            NodeKind.Xor => CanAnchorExactlyOnce(n) ? 1 : 0,
            _ => inputPower,
        };

    #endregion

    #region Move validation (used by interaction in a later phase)

    public bool CanAddPower(PowerNodeComponent slot, int power)
    {
        if (!IsActive || slot == null || !_nodes.Contains(slot)) return false;
        if (HasOverlaps(slot, power)) return false;
        return CanAnchorFind(slot);
    }

    public bool CanRemovePower(PowerNodeComponent slot)
    {
        if (!IsActive || slot == null || !_nodes.Contains(slot)) return false;

        slot.Removed = true;
        foreach (var node in _nodes)
        {
            if (node == slot) continue;
            if (GetPower(node) > 0 && !CanAnchorFind(node))
            {
                slot.Removed = false;
                return false;
            }
        }
        slot.Removed = false;
        return true;
    }

    public bool CanAnchorFind(PowerNodeComponent destination)
    {
        if (destination.IsAnchor && !destination.Removed) return true;

        foreach (var slot in _nodes)
        {
            if (slot == destination || !slot.IsAnchor || slot.Removed) continue;
            if (Search(slot, destination, slot.PlacedTokenPower + 1, new HashSet<PowerNodeComponent>()) != null)
                return true;
        }
        return false;
    }

    public bool CanAnchorFindTwice(PowerNodeComponent destination)
    {
        if (destination.IsAnchor && !destination.Removed) return true;

        foreach (var slot in _nodes)
        {
            if (slot == destination || !slot.IsAnchor || slot.Removed) continue;

            var round1 = Search(slot, destination, slot.PlacedTokenPower + 1, new HashSet<PowerNodeComponent>());
            if (round1 == null) continue;

            var blocked = round1.Where(s => s != slot).ToHashSet();
            if (Search(slot, destination, slot.PlacedTokenPower + 1, blocked) != null)
                return true;
        }
        return false;
    }

    public bool CanAnchorExactlyOnce(PowerNodeComponent destination)
    {
        if (destination.IsAnchor && !destination.Removed) return true;

        foreach (var slot in _nodes)
        {
            if (slot == destination || !slot.IsAnchor || slot.Removed) continue;

            var round1 = Search(slot, destination, slot.PlacedTokenPower + 1, new HashSet<PowerNodeComponent>());
            if (round1 == null) continue;

            var blocked = round1.Where(s => s != slot).ToHashSet();
            return Search(slot, destination, slot.PlacedTokenPower + 1, blocked) == null;
        }
        return false;
    }

    private List<PowerNodeComponent> Search(
        PowerNodeComponent start, PowerNodeComponent destination, int maxDepth, HashSet<PowerNodeComponent> seen)
    {
        if (maxDepth <= 0) return null;
        if (start == destination) return new List<PowerNodeComponent> { start };
        if (!seen.Add(start)) return null;

        if (start.NodeKind == NodeKind.And && !CanAnchorFindTwice(start)) return null;
        if (start.NodeKind == NodeKind.Xor && !CanAnchorExactlyOnce(start)) return null;

        var currentPower = start.Removed ? 0 : GetStructuralPower(start);
        currentPower = Math.Min(currentPower, ConvertPower(start, maxDepth));

        foreach (var connect in _lines[start])
        {
            bool isLocked = connect.Locks != null && connect.Locks.Any(l => l.IsLocked);
            bool hasConflict = connect.CompetingConnections != null && connect.CompetingConnections.Any(c => c.IsActive);
            if (isLocked || hasConflict) continue;

            var res = Search(connect.To, destination, Math.Max(maxDepth - 1, currentPower), seen);
            if (res != null)
            {
                res.Insert(0, start);
                return res;
            }
        }
        return null;
    }

    /// <summary>Structural source strength used by reachability search (placed-token strength).</summary>
    private static int GetStructuralPower(PowerNodeComponent n)
        => n.PlacedTokenPower;

    public bool HasOverlaps(PowerNodeComponent slot, int power, HashSet<PowerNodeComponent> seen = null)
    {
        if (power <= 0) return false;
        seen ??= new HashSet<PowerNodeComponent>();
        if (!seen.Add(slot)) return false;

        foreach (var connect in _lines[slot])
        {
            if (connect.CompetingConnections != null && connect.CompetingConnections.Any(c => c.IsActive))
                return true;
            if (HasOverlaps(connect.To, power - 1, seen))
                return true;
        }
        return false;
    }

    #endregion
}
