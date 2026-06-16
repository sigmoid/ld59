using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ld59.UI.Powergrid;

/// <summary>
/// Headless assertions for the Powergrid pulse simulation (POWERGRID_DESIGN.md → v2 mechanics).
/// Each test builds a tiny graph in code, runs the sim, and checks the expected outcome.
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(string desc, bool cond)
    {
        if (cond) { _passed++; Console.WriteLine($"  PASS  {desc}"); }
        else      { _failed++; Console.WriteLine($"  FAIL  {desc}"); }
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "design") { DesignReport(); return 0; }

        Console.WriteLine("Powergrid pulse-sim tests\n");

        StraightLineSolves();
        NoEmitterNeverSolves();
        NormalNodeCancelsTwoSimultaneous();
        GoalArrivalWinsEvenWhenDoubled();
        AndGateNeedsTwo();
        XorGateNeedsExactlyOne();
        SplitReachesAllOutputs();
        CrossingCancelsSimultaneous();
        CrossingAllowedDifferentTicks();
        LockLatchesOpenOnceKeyFires();
        LockedWithoutKeyStaysClosed();
        TickCapHaltsCycle();
        ContentMirrorAssemblySolvable();
        ContentLockedMirrorSolvable();
        DelayLinesUpAndGate();
        DelayDodgesCrossing();

        Console.WriteLine($"\n{_passed}/{_passed + _failed} passed.");
        return _failed == 0 ? 0 : 1;
    }

    // ── Hard-level design: brute-force solver over (anchor subset × delays) ───

    /// <summary>Every solving configuration of a level, given a token budget and max delay.
    /// Each solution is "anchorName+delay, ...". Used to verify a level is solvable, requires delay,
    /// and isn't trivially over-solvable.</summary>
    private static List<string> Solve(Builder template, int maxEmitters, int maxDelay)
    {
        var g = template.Build();
        var anchors = g.Nodes.Where(n => n.IsAnchor).ToList();
        var sols = new List<string>();
        int n = anchors.Count;

        for (int mask = 1; mask < (1 << n); mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) > maxEmitters) continue;
            var chosen = new List<PowerNodeComponent>();
            for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) chosen.Add(anchors[i]);

            var delays = new int[chosen.Count];
            while (true)
            {
                foreach (var a in anchors) { a.PlacedTokenPower = 0; a.PlacedTokenDelay = 0; }
                for (int i = 0; i < chosen.Count; i++) { chosen[i].PlacedTokenPower = 1; chosen[i].PlacedTokenDelay = delays[i]; }

                g.RunToCompletion();
                if (g.IsSolved)
                    sols.Add(string.Join(", ", chosen.Select((a, i) => $"{a.Entity.Name}+{delays[i]}")));

                int k = 0;
                while (k < delays.Length) { if (++delays[k] <= maxDelay) break; delays[k] = 0; k++; }
                if (k == delays.Length) break;
            }
        }
        return sols;
    }

    private static bool AnySolutionAllZeroDelay(List<string> sols)
        => sols.Any(s => s.Split(", ").All(t => t.EndsWith("+0")));

    private static void DesignReport()
    {
        void Report(string name, Builder t, int budget, int maxDelay)
        {
            var sols = Solve(t, budget, maxDelay);
            Console.WriteLine($"\n=== {name} (budget {budget}, maxDelay {maxDelay}) ===");
            Console.WriteLine($"  solutions: {sols.Count}   requires-delay: {!AnySolutionAllZeroDelay(sols)}");
            foreach (var s in sols.Take(12)) Console.WriteLine($"    {s}");
            if (sols.Count > 12) Console.WriteLine($"    ... (+{sols.Count - 12} more)");
        }

        Report("crosswire", Crosswire(), budget: 2, maxDelay: 3);
        Report("relay-race", RelayRace(), budget: 2, maxDelay: 4);
        Report("gatekeeper", Gatekeeper(), budget: 2, maxDelay: 4);
        Report("interceptor", Interceptor(), budget: 2, maxDelay: 4);
    }

    // Level templates (no emitters baked; the solver chooses anchors + delays). ──────

    /// <summary>AND gate fed by two crossing paths of unequal length: must delay the short path so the
    /// pulses both arrive at the gate together AND traverse the crossing on different ticks.</summary>
    private static Builder Crosswire()
    {
        var b = new Builder();
        b.Node("A", 0, 2, anchor: true);
        b.Node("p1", 3, 2); b.Node("p2", 6, -1);
        b.Node("B", 0, -2, anchor: true);
        b.Node("q1", 3, -2); b.Node("q2", 6, 1); b.Node("q3", 7, 0);
        b.Node("M", 8, 0, kind: NodeKind.And);
        b.Node("G", 10, 0, goal: true);
        b.Edge("A", "p1"); b.Edge("p1", "p2"); b.Edge("p2", "M");
        b.Edge("B", "q1"); b.Edge("q1", "q2"); b.Edge("q2", "q3"); b.Edge("q3", "M");
        b.Edge("M", "G");
        return b;
    }

    /// <summary>Two anchors, three AND inputs reached through a shared relay; tuned in DesignReport.</summary>
    private static Builder RelayRace()
    {
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true);
        b.Node("B", 0, -3, anchor: true);
        b.Node("r", 2, 0);
        b.Node("u", 4, 1);
        b.Node("d", 4, -1);
        b.Node("M", 6, 0, kind: NodeKind.And);
        b.Node("G", 8, 0, goal: true);
        b.Edge("A", "r"); b.Edge("r", "u"); b.Edge("r", "d");
        b.Edge("u", "M"); b.Edge("d", "M");
        b.Edge("B", "d");
        b.Edge("M", "G");
        return b;
    }

    /// <summary>AND gate whose bottom input crosses a key-locked edge; key lit by a separate path.</summary>
    private static Builder Gatekeeper()
    {
        var b = new Builder();
        b.Node("A", 0, 2, anchor: true);
        b.Node("t1", 3, 2);
        b.Node("K", 3, 0);              // key
        b.Node("B", 0, -2, anchor: true);
        b.Node("b1", 3, -2);
        b.Node("M", 6, 0, kind: NodeKind.And);
        b.Node("G", 8, 0, goal: true);
        b.Edge("A", "t1"); b.Edge("A", "K"); b.Edge("t1", "M");
        b.Edge("B", "b1"); b.Edge("b1", "M");
        b.Edge("M", "G");
        b.Lock(new Vector2(4.5f, -0.7f), new Vector2(4.5f, 0.7f), key: "K"); // crosses b1->M
        return b;
    }

    /// <summary>Single emitter's split self-cancels at an XOR; a second emitter must annihilate one
    /// branch (timed) so exactly one pulse reaches the XOR.</summary>
    private static Builder Interceptor()
    {
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true);
        b.Node("u1", 2, 1.5f); b.Node("u2", 4, 1.5f);
        b.Node("l1", 2, -1.5f); b.Node("l2", 4, -1.5f);
        b.Node("X", 6, 0, kind: NodeKind.Xor);
        b.Node("G", 8, 0, goal: true);
        b.Node("B", 2, -3.5f, anchor: true);
        b.Edge("A", "u1"); b.Edge("u1", "u2"); b.Edge("u2", "X");
        b.Edge("A", "l1"); b.Edge("l1", "l2"); b.Edge("l2", "X");
        b.Edge("B", "l2"); // B can annihilate the lower branch at l2 if timed to coincide
        b.Edge("X", "G");
        return b;
    }

    // ── Scenarios ────────────────────────────────────────────────────────────

    private static void StraightLineSolves()
    {
        Console.WriteLine("Straight line A->B->G solves, B lit before G");
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true, token: 1);
        b.Node("B", 2, 0);
        b.Node("G", 4, 0, goal: true);
        b.Edge("A", "B"); b.Edge("B", "G");
        var g = b.Build();

        g.StartRun();
        Check("t0: anchor A lit", g.Nodes_Find("A").IsActive);
        g.StepTick();
        Check("t1: B lit", g.Nodes_Find("B").IsActive);
        g.StepTick();
        Check("t2: solved when pulse reaches G", g.IsSolved);
    }

    private static void NoEmitterNeverSolves()
    {
        Console.WriteLine("No emitter token => run never starts/solves");
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true); // no token
        b.Node("G", 2, 0, goal: true);
        b.Edge("A", "G");
        var g = b.Build();
        g.RunToCompletion();
        Check("not solved", !g.IsSolved);
        Check("run finished immediately", g.IsFinished && !g.IsRunning);
    }

    private static void NormalNodeCancelsTwoSimultaneous()
    {
        Console.WriteLine("Two pulses arriving together at a Normal node annihilate (no onward emit)");
        var b = new Builder();
        b.Node("A", 0, 2, anchor: true, token: 1);
        b.Node("E", 0, -2, anchor: true, token: 1);
        b.Node("M", 2, 0);            // normal junction
        b.Node("G", 4, 0, goal: true);
        b.Edge("A", "M"); b.Edge("E", "M"); b.Edge("M", "G");
        var g = b.Build();
        g.RunToCompletion();
        Check("not solved (M cancelled, never re-emitted)", !g.IsSolved);
    }

    private static void GoalArrivalWinsEvenWhenDoubled()
    {
        Console.WriteLine("A goal solves on arrival even if two pulses arrive together");
        var b = new Builder();
        b.Node("A", 0, 2, anchor: true, token: 1);
        b.Node("E", 0, -2, anchor: true, token: 1);
        b.Node("G", 2, 0, goal: true); // two simultaneous arrivals land on the goal
        b.Edge("A", "G"); b.Edge("E", "G");
        var g = b.Build();
        g.RunToCompletion();
        Check("solved (arrival wins, no goal cancellation)", g.IsSolved);
    }

    private static void AndGateNeedsTwo()
    {
        Console.WriteLine("AND gate emits only when both inputs arrive together");
        // Two inputs -> solved.
        var b = new Builder();
        b.Node("A", 0, 2, anchor: true, token: 1);
        b.Node("E", 0, -2, anchor: true, token: 1);
        b.Node("M", 2, 0, kind: NodeKind.And);
        b.Node("G", 4, 0, goal: true);
        b.Edge("A", "M"); b.Edge("E", "M"); b.Edge("M", "G");
        var g = b.Build();
        g.RunToCompletion();
        Check("two inputs => AND emits => solved", g.IsSolved);

        // One input -> not solved.
        var b2 = new Builder();
        b2.Node("A", 0, 2, anchor: true, token: 1);
        b2.Node("E", 0, -2, anchor: true); // no token
        b2.Node("M", 2, 0, kind: NodeKind.And);
        b2.Node("G", 4, 0, goal: true);
        b2.Edge("A", "M"); b2.Edge("E", "M"); b2.Edge("M", "G");
        var g2 = b2.Build();
        g2.RunToCompletion();
        Check("one input => AND stays off => not solved", !g2.IsSolved);
    }

    private static void XorGateNeedsExactlyOne()
    {
        Console.WriteLine("XOR gate emits on exactly one input, cancels on two");
        // One input -> solved.
        var b = new Builder();
        b.Node("A", 0, 2, anchor: true, token: 1);
        b.Node("M", 2, 0, kind: NodeKind.Xor);
        b.Node("G", 4, 0, goal: true);
        b.Edge("A", "M"); b.Edge("M", "G");
        var g = b.Build();
        g.RunToCompletion();
        Check("one input => XOR emits => solved", g.IsSolved);

        // Two inputs -> not solved.
        var b2 = new Builder();
        b2.Node("A", 0, 2, anchor: true, token: 1);
        b2.Node("E", 0, -2, anchor: true, token: 1);
        b2.Node("M", 2, 0, kind: NodeKind.Xor);
        b2.Node("G", 4, 0, goal: true);
        b2.Edge("A", "M"); b2.Edge("E", "M"); b2.Edge("M", "G");
        var g2 = b2.Build();
        g2.RunToCompletion();
        Check("two inputs => XOR cancels => not solved", !g2.IsSolved);
    }

    private static void SplitReachesAllOutputs()
    {
        Console.WriteLine("Fan-out splits to all outputs (both C and D light); one path reaches the goal");
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true, token: 1);
        b.Node("B", 2, 0);
        b.Node("C", 4, 1);
        b.Node("D", 4, -1);            // dead end
        b.Node("G", 6, 1, goal: true);
        b.Edge("A", "B"); b.Edge("B", "C"); b.Edge("B", "D"); b.Edge("C", "G");
        var g = b.Build();

        g.StartRun();   // t0
        g.StepTick();   // t1: B
        g.StepTick();   // t2: split -> C and D
        Check("t2: C lit (split)", g.Nodes_Find("C").IsActive);
        Check("t2: D lit (split)", g.Nodes_Find("D").IsActive);
        g.StepTick();   // t3: C -> G
        Check("t3: solved via C branch", g.IsSolved);
    }

    private static void CrossingCancelsSimultaneous()
    {
        Console.WriteLine("Two physically crossing edges energised on the same tick cancel each other");
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true, token: 1);
        b.Node("B", 2, 2, goal: true);
        b.Node("E", 2, 0, anchor: true, token: 1);
        b.Node("F", 0, 2);
        b.Edge("A", "B"); // (0,0)->(2,2)
        b.Edge("E", "F"); // (2,0)->(0,2)  crosses A->B at (1,1)
        var g = b.Build();
        g.RunToCompletion();
        Check("not solved (A->B cancelled by simultaneous crossing)", !g.IsSolved);
    }

    private static void CrossingAllowedDifferentTicks()
    {
        Console.WriteLine("The same crossing is usable when the two edges energise on different ticks");
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true, token: 1);
        b.Node("B", 2, 2, goal: true);   // A->B traverses t0->t1
        b.Node("E0", 2, -2, anchor: true, token: 1);
        b.Node("E", 2, 0);               // E0->E (t0->t1), then E->F (t1->t2)
        b.Node("F", 0, 2);
        b.Edge("A", "B");
        b.Edge("E0", "E");
        b.Edge("E", "F"); // crosses A->B, but one tick later
        var g = b.Build();
        g.RunToCompletion();
        Check("solved (A->B free at t0, crossing edge fires t1)", g.IsSolved);
    }

    private static void LockLatchesOpenOnceKeyFires()
    {
        Console.WriteLine("A locked edge latches open once its key node fires earlier in the run");
        var g = BuildLockScenario(connectKey: true);
        g.RunToCompletion();
        Check("solved (key K fires t1, opens C->G before pulse arrives t3)", g.IsSolved);
    }

    private static void LockedWithoutKeyStaysClosed()
    {
        Console.WriteLine("Without powering the key, the locked edge blocks the pulse");
        var g = BuildLockScenario(connectKey: false);
        g.RunToCompletion();
        Check("not solved (lock never opens)", !g.IsSolved);
    }

    /// <summary>
    /// A -> B -> C -> G with the C->G edge locked by key K. A also fans out to K (if connectKey).
    /// K fires at t1 and latches the lock open well before the main pulse reaches C->G at t3.
    /// </summary>
    private static PuzzleGraph BuildLockScenario(bool connectKey)
    {
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true, token: 1);
        b.Node("K", 0, -2);
        b.Node("B", 2, 0);
        b.Node("C", 4, 0);
        b.Node("G", 6, 0, goal: true);
        b.Edge("A", "B"); b.Edge("B", "C"); b.Edge("C", "G");
        if (connectKey) b.Edge("A", "K");
        // Lock segment crosses only the padded C->G edge (x in [4.4, 5.6] at y=0).
        b.Lock(new Vector2(5, -1), new Vector2(5, 1), key: "K");
        return b.Build();
    }

    // ── Lever 1: player-set emission delay ────────────────────────────────────

    private static void DelayLinesUpAndGate()
    {
        Console.WriteLine("Delay: staggering a short-path emitter lines it up with a long path at an AND gate");
        // A->M is 1 hop (arrives t1); B->X->M is 2 hops (arrives t2). AND needs both same tick.
        Builder Make(int aDelay)
        {
            var b = new Builder();
            b.Node("A", 0, 2, anchor: true, token: 1, delay: aDelay);
            b.Node("B", 0, -2, anchor: true, token: 1);
            b.Node("X", 2, -2);
            b.Node("M", 4, 0, kind: NodeKind.And);
            b.Node("G", 6, 0, goal: true);
            b.Edge("A", "M");
            b.Edge("B", "X"); b.Edge("X", "M");
            b.Edge("M", "G");
            return b;
        }
        Check("no delay => arrivals mistimed => not solved", !Make(0).Build().RunSolved());
        Check("A delayed by 1 => both arrive t2 => AND fires => solved", Make(1).Build().RunSolved());
    }

    private static void DelayDodgesCrossing()
    {
        Console.WriteLine("Delay: offsetting one pulse lets it use a crossing the other would have cancelled");
        Builder Make(int eDelay)
        {
            var b = new Builder();
            b.Node("A", 0, 0, anchor: true, token: 1);
            b.Node("B", 2, 2, goal: true);
            b.Node("E", 2, 0, anchor: true, token: 1, delay: eDelay);
            b.Node("F", 0, 2);
            b.Edge("A", "B"); // crosses E->F
            b.Edge("E", "F");
            return b;
        }
        Check("both fire t0 => crossing cancels => not solved", !Make(0).Build().RunSolved());
        Check("E delayed by 1 => A->B free at t0 => solved", Make(1).Build().RunSolved());
    }

    // ── Authored-content solvability (mirrors the level XML topology exactly) ──

    private static void ContentMirrorAssemblySolvable()
    {
        Console.WriteLine("Content: mirror-assembly (AND) solves only when branch lengths match");
        Check("matched short pair {TA,Ba} solves", Assembly("TA", "Ba").Build().RunSolved());
        Check("matched long pair {Tm,Bm} solves", Assembly("Tm", "Bm").Build().RunSolved());
        Check("mismatched {TA,Bm} fails (pulses arrive on different ticks)", !Assembly("TA", "Bm").Build().RunSolved());
        Check("one branch only {TA,Tm} fails (AND needs both inputs)", !Assembly("TA", "Tm").Build().RunSolved());
    }

    private static void ContentLockedMirrorSolvable()
    {
        Console.WriteLine("Content: locked-mirror (AND + key-locked path) solves via the long bottom path");
        Check("{TA,BL} solves: key opens lock in time, lengths match at t3", LockedMirror("TA", "BL").Build().RunSolved());
        Check("decoy {TA,BS} fails: short path arrives a tick early", !LockedMirror("TA", "BS").Build().RunSolved());
        Check("{BL,BS} fails: no top input to the AND", !LockedMirror("BL", "BS").Build().RunSolved());
    }

    /// <summary>mirror-assembly.xml topology; emitters placed on the named anchors.</summary>
    private static Builder Assembly(params string[] emitters)
    {
        var e = new HashSet<string>(emitters);
        int Tok(string n) => e.Contains(n) ? 1 : 0;

        var b = new Builder();
        b.Node("S", 0, 0, kind: NodeKind.And);
        b.Node("goal", 2, 0, goal: true);
        b.Node("TU", 0, 2);
        b.Node("BU", 0, -2);
        b.Node("TA", -3, 2, anchor: true, token: Tok("TA"));
        b.Node("Tmid", -2, 4);
        b.Node("Tm", -4, 4, anchor: true, token: Tok("Tm"));
        b.Node("Ba", -3, -2, anchor: true, token: Tok("Ba"));
        b.Node("Bmid", -2, -4);
        b.Node("Bm", -4, -4, anchor: true, token: Tok("Bm"));

        b.Edge("S", "goal");
        b.Edge("TU", "S"); b.Edge("BU", "S");
        b.Edge("TA", "TU"); b.Edge("Tmid", "TU"); b.Edge("Tm", "Tmid");
        b.Edge("Ba", "BU"); b.Edge("Bmid", "BU"); b.Edge("Bm", "Bmid");
        return b;
    }

    /// <summary>locked-mirror.xml topology; emitters placed on the named anchors.</summary>
    private static Builder LockedMirror(params string[] emitters)
    {
        var e = new HashSet<string>(emitters);
        int Tok(string n) => e.Contains(n) ? 1 : 0;

        var b = new Builder();
        b.Node("S", 0, 0, kind: NodeKind.And);
        b.Node("goal", 2, 0, goal: true);
        b.Node("TU", 0, 2);
        b.Node("BU", 0, -2);
        b.Node("TA", -4, 2, anchor: true, token: Tok("TA"));
        b.Node("Tmid", -2, 2);
        b.Node("K", -2, 0);
        b.Node("BL", -4, -2, anchor: true, token: Tok("BL"));
        b.Node("Bmid", -2, -2);
        b.Node("BS", -1, -4, anchor: true, token: Tok("BS"));

        b.Edge("S", "goal");
        b.Edge("TU", "S"); b.Edge("BU", "S");
        b.Edge("TA", "Tmid"); b.Edge("TA", "K"); b.Edge("Tmid", "TU");
        b.Edge("BL", "Bmid"); b.Edge("Bmid", "BU");
        b.Edge("BS", "BU");
        b.Lock(new Vector2(-1, -2.7f), new Vector2(-1, -1.3f), key: "K");
        return b;
    }

    private static void TickCapHaltsCycle()
    {
        Console.WriteLine("A pulse circulating a cycle halts at the tick cap (no goal, no hang)");
        var b = new Builder();
        b.Node("A", 0, 0, anchor: true, token: 1);
        b.Node("B", 2, 0);
        b.Edge("A", "B"); b.Edge("B", "A"); // cycle, no goal
        var g = b.Build(tickCap: 8);
        g.RunToCompletion();
        Check("not solved", !g.IsSolved);
        Check("run finished", g.IsFinished && !g.IsRunning);
        Check("halted at tick cap", g.CurrentTick == 8);
    }
}

/// <summary>Builds a <see cref="PuzzleGraph"/> from a tiny in-code spec for testing.</summary>
internal sealed class Builder
{
    private readonly Dictionary<string, PowerNodeComponent> _map = new();
    private readonly List<PowerNodeComponent> _nodes = new();
    private readonly List<EdgeLockComponent> _locks = new();

    public void Node(string name, float x, float y,
                     bool anchor = false, bool goal = false,
                     NodeKind kind = NodeKind.Normal, int token = 0, int delay = 0)
    {
        var entity = new Entity { Name = name, Position = new Vector2(x, y) };
        var node = new PowerNodeComponent
        {
            IsAnchor = anchor,
            IsGoal = goal,
            NodeKind = kind,
            PlacedTokenPower = token,
            PlacedTokenDelay = delay,
        };
        node.Entity = entity;
        _map[name] = node;
        _nodes.Add(node);
    }

    public void Edge(string from, string to) => _map[from].OutgoingNodeNames.Add(to);

    public void Lock(Vector2 a, Vector2 b, string key)
        => _locks.Add(new EdgeLockComponent
        {
            PointA = a,
            PointB = b,
            KeyNode = key,
            KeyNodeRef = _map.TryGetValue(key, out var k) ? k : null,
        });

    public PuzzleGraph Build(int tickCap = 64)
    {
        var g = new PuzzleGraph(_nodes, _locks, n => _map.TryGetValue(n, out var node) ? node : null)
        {
            TickCap = tickCap,
        };
        g.ResetRun();
        return g;
    }
}

/// <summary>Small lookup helper for tests (PuzzleGraph exposes Nodes; this finds one by name).</summary>
internal static class PuzzleGraphTestExtensions
{
    public static PowerNodeComponent Nodes_Find(this PuzzleGraph g, string name)
    {
        foreach (var n in g.Nodes)
            if (n.Entity.Name == name) return n;
        throw new KeyNotFoundException(name);
    }

    /// <summary>Runs the graph to completion and reports whether a goal was reached.</summary>
    public static bool RunSolved(this PuzzleGraph g)
    {
        g.RunToCompletion();
        return g.IsSolved;
    }
}
