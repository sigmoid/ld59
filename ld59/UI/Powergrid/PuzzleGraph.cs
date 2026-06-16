using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace ld59.UI.Powergrid;

/// <summary>
/// Runtime model + <b>pulse simulation</b> for one sub-puzzle: a directed graph of
/// <see cref="PowerNodeComponent"/>s connected by <see cref="Connection"/>s, with optional
/// <see cref="EdgeLockComponent"/>s. Rebuilt each session from the scene's components (never serialized).
///
/// <para>v2 mechanics (see POWERGRID_DESIGN.md → "Pulse Simulation Redesign"). The static instant-power
/// model is gone. The player drops emitter tokens on anchors and runs a time-stepped simulation:</para>
/// <list type="bullet">
///   <item>Discrete clock, <b>one edge per tick</b>. Anchors with a token emit a pulse at tick 0.</item>
///   <item>Pulses travel forward along directed edges and <b>split to all outputs</b> at a fan-out.</item>
///   <item>Pulses don't weaken; a node may <b>re-fire</b>, so a <see cref="TickCap"/> bounds the run.</item>
///   <item>Per-tick arrival counts drive cancellation/gates: a Normal node emits on exactly 1 arrival
///         and annihilates on ≥ 2; <b>AND</b> emits on ≥ 2; <b>XOR</b> emits on exactly 1.</item>
///   <item>Two <b>physically crossing</b> edges energised on the same tick cancel each other.</item>
///   <item>A lock <b>latches open</b> once its key node has fired this run.</item>
///   <item><b>Arrival wins</b>: any pulse reaching a goal solves the puzzle.</item>
/// </list>
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

    /// <summary>Maximum simulation ticks before a run halts (bounds cyclic/echoing pulses). Editable
    /// per puzzle; the controller sets this from the level config.</summary>
    public int TickCap = 64;

    /// <summary>Whether this sub-puzzle is currently active (witness-style chaining). Inactive puzzles
    /// don't run. Distinct from a node's per-tick <see cref="PowerNodeComponent.IsActive"/>.</summary>
    public bool IsActive = true;

    /// <summary>True once any pulse has reached a goal node during the current/last run.</summary>
    public bool IsSolved { get; private set; }

    /// <summary>True while a run is in progress (between <see cref="StartRun"/> and the tick the run halts).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True once a run has halted (solved, ran dry, or hit the tick cap).</summary>
    public bool IsFinished { get; private set; }

    /// <summary>The tick most recently produced by <see cref="StepTick"/> (0 = just-emitted anchors).</summary>
    public int CurrentTick { get; private set; }

    /// <summary>Nodes emitting on <see cref="CurrentTick"/> (the wavefront source for the next step).</summary>
    private readonly List<PowerNodeComponent> _firingNow = new();

    /// <summary>Placed emitters with their (player-set) fire tick, captured at <see cref="StartRun"/>.
    /// An emitter is injected as a fresh source on the tick equal to its delay.</summary>
    private readonly List<(PowerNodeComponent node, int delay)> _emitters = new();
    private int _maxDelay;

    /// <summary>Sub-puzzle id (smallest node name in the component); used for activation chaining.</summary>
    public string Id { get; set; } = "default";

    /// <summary>When false (default), all nodes are visible from the start. When true, the fog/discovery
    /// mechanic applies (nodes hidden until a pulse reaches them).</summary>
    public bool DiscoveryEnabled { get; set; }

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
        ResetRun();
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

                // Edges meeting at a shared node are not "crossing" (so a fan-out's own outputs never
                // cancel one another).
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

    #region Pulse simulation

    /// <summary>Clears all run state back to the placement phase: nothing lit, locks closed, unsolved.</summary>
    public void ResetRun()
    {
        IsRunning = false;
        IsFinished = false;
        IsSolved = false;
        CurrentTick = 0;
        _firingNow.Clear();
        _emitters.Clear();
        _maxDelay = 0;

        foreach (var n in _nodes)
        {
            n.IsActive = false;
            n.FiredThisRun = false;
            // Discovery: with fog off everything is visible; with fog on, only anchors/goals start revealed.
            n.Discovered = !DiscoveryEnabled || n.IsAnchor || n.IsGoal;
        }
        foreach (var c in _allConnections) c.IsActive = false;
        foreach (var l in _locks) l.IsLocked = true;
    }

    /// <summary>Begins a run: each placed emitter is scheduled to fire on its delay tick; delay-0
    /// emitters fire immediately at tick 0.</summary>
    public void StartRun()
    {
        ResetRun();
        if (!IsActive) { IsFinished = true; return; }

        foreach (var n in _nodes)
            if (n.IsAnchor && n.PlacedTokenPower > 0)
            {
                int delay = Math.Max(0, n.PlacedTokenDelay);
                _emitters.Add((n, delay));
                if (delay > _maxDelay) _maxDelay = delay;
            }

        foreach (var (node, delay) in _emitters)
            if (delay == 0) FireSource(node);

        LatchLocks();

        IsRunning = _emitters.Count > 0 && !IsSolved && TickCap > 0;
        IsFinished = !IsRunning;
    }

    /// <summary>Marks a node as a fresh pulse source on the current tick (an emitter firing, or a
    /// re-emitting junction): adds it to the firing set and lights it.</summary>
    private void FireSource(PowerNodeComponent node)
    {
        if (!_firingNow.Contains(node)) _firingNow.Add(node);
        node.FiredThisRun = true;
        node.IsActive = true;
        node.Discovered = true;
        if (node.IsGoal) IsSolved = true;   // arrival wins (degenerate anchor-goal)
    }

    /// <summary>
    /// Advances the simulation one tick: every node firing this tick energises its valid outbound
    /// edges (split), crossing edges energised the same tick cancel, then arriving pulses are tallied
    /// per node and the cancellation/gate rules decide the next firing set. Returns true while the run
    /// can continue (more firing, no win, cap not reached).
    /// </summary>
    public bool StepTick()
    {
        if (!IsRunning) return false;

        // 1. Candidate traversals: outbound edges of the current firing set whose locks are open.
        var candidates = new List<Connection>();
        foreach (var n in _firingNow)
            foreach (var c in _lines[n])
                if (!HasActiveLock(c))
                    candidates.Add(c);

        var candidateSet = new HashSet<Connection>(candidates);

        // 2. Crossing resolution: two crossing edges energised on the same tick cancel each other.
        var survivors = new List<Connection>(candidates.Count);
        foreach (var c in candidates)
        {
            if (c.CompetingConnections != null && c.CompetingConnections.Any(candidateSet.Contains))
                continue;
            survivors.Add(c);
        }

        // 3. Clear last tick's transient lit/energised state.
        foreach (var n in _nodes) n.IsActive = false;
        foreach (var conn in _allConnections) conn.IsActive = false;

        // 4. Tally arrivals (this tick's traversals land on their targets next tick).
        var arrivals = new Dictionary<PowerNodeComponent, int>();
        foreach (var c in survivors)
        {
            c.IsActive = true;   // wire energised during this traversal
            arrivals[c.To] = arrivals.GetValueOrDefault(c.To) + 1;
        }

        // 5. Apply cancellation / gate rules → the next firing set; detect goal arrivals.
        var nextFiring = new List<PowerNodeComponent>();
        foreach (var (node, count) in arrivals)
        {
            node.IsActive = true;       // lit: a pulse reached it
            node.Discovered = true;

            if (node.IsGoal) IsSolved = true;   // arrival wins, regardless of cancellation

            if (Emits(node, count))
            {
                node.FiredThisRun = true;
                nextFiring.Add(node);
            }
        }

        CurrentTick++;
        _firingNow.Clear();
        _firingNow.AddRange(nextFiring);

        // Inject any emitters scheduled to fire on this tick (delayed pulses join the wavefront).
        foreach (var (node, delay) in _emitters)
            if (delay == CurrentTick) FireSource(node);

        LatchLocks();   // a node that fired this tick may open its lock for the next

        // Keep running while pulses are live OR emitters are still scheduled to fire later.
        bool more = !IsSolved && CurrentTick < TickCap
                    && (_firingNow.Count > 0 || CurrentTick < _maxDelay);
        if (!more)
        {
            IsRunning = false;
            IsFinished = true;
        }
        return more;
    }

    /// <summary>Runs from a clean start to the halt condition (solved, ran dry, or tick cap).</summary>
    public void RunToCompletion()
    {
        StartRun();
        // +2 guard is belt-and-suspenders; StepTick already enforces the cap.
        for (int guard = 0; guard <= TickCap + 2 && StepTick(); guard++) { }
    }

    /// <summary>Does an arriving node re-emit, given how many pulses reached it this tick?</summary>
    private static bool Emits(PowerNodeComponent n, int arrivalCount) => n.NodeKind switch
    {
        NodeKind.And => arrivalCount >= 2,
        NodeKind.Xor => arrivalCount == 1,
        _            => arrivalCount == 1,   // Normal: lone pulse passes; 2+ annihilate.
    };

    private static bool HasActiveLock(Connection line)
        => line.Locks != null && line.Locks.Any(l => l.IsLocked);

    /// <summary>Latch open any lock whose key node has fired this run; once open it stays open.</summary>
    private void LatchLocks()
    {
        foreach (var l in _locks)
            if (l.IsLocked && l.KeyNodeRef != null && l.KeyNodeRef.FiredThisRun)
                l.IsLocked = false;
    }

    #endregion

    #region Placement (interaction)

    /// <summary>Tokens may only be placed on anchor nodes, and placement is never rejected (the run
    /// decides success). The <paramref name="power"/> is ignored by the sim (single emitter type).</summary>
    public bool CanAddPower(PowerNodeComponent slot, int power)
        => IsActive && slot != null && slot.IsAnchor && _nodes.Contains(slot);

    /// <summary>An emitter can always be picked back up.</summary>
    public bool CanRemovePower(PowerNodeComponent slot)
        => IsActive && slot != null && _nodes.Contains(slot);

    #endregion
}
