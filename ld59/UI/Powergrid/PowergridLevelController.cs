using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;

namespace ld59.UI.Powergrid;

/// <summary>
/// Owns the loaded <see cref="Scene"/> for a Powergrid level and bridges the serialized component
/// data to the runtime solver. Reconstructed each session; never serialized.
///
/// Sub-puzzles are determined automatically: each connected component of the node graph (treating
/// connections as undirected) is one puzzle. A component's id is the smallest node name in it.
/// Activation links are stored as node-name pairs and resolved to the components those nodes belong
/// to, so they survive edits/regrouping. Locks attach to the component of their key node (or the
/// nearest node).
/// </summary>
public class PowergridLevelController
{
    private readonly Scene _scene;
    private readonly List<PuzzleGraph> _graphs = new();
    private readonly Dictionary<string, PuzzleGraph> _graphsById = new();
    private readonly Dictionary<string, string> _componentOfNode = new(); // node name -> component id
    private readonly Dictionary<string, List<string>> _prereqs = new();   // component id -> prereq component ids

    public Scene Scene => _scene;
    public IReadOnlyList<PuzzleGraph> Graphs => _graphs;

    public IReadOnlyList<int> InitialInventory { get; private set; } = new List<int>();
    public int HoldingSlots { get; private set; } = 3;

    private PowergridLevelComponent _levelConfig;

    /// <summary>Activation edges as node-name pairs (node-in-puzzle-A enables node-in-puzzle-B).</summary>
    public IReadOnlyList<(string From, string To)> ActivationEdges { get; private set; } = new List<(string, string)>();

    public PuzzleGraph GraphOf(PowerNodeComponent node) => _graphs.FirstOrDefault(g => g.Nodes.Contains(node));
    public PuzzleGraph GraphById(string id) => _graphsById.TryGetValue(id, out var g) ? g : null;
    public PuzzleGraph GraphContainingNode(string name)
        => _componentOfNode.TryGetValue(name, out var id) ? GraphById(id) : null;

    public PowergridLevelController(Scene scene)
    {
        _scene = scene;
        Build();
    }

    private void Build()
    {
        var entities = _scene.GetEntities();
        var nodeEntities = entities.Where(e => e.GetComponent<PowerNodeComponent>() != null).ToList();
        var nodesByName = nodeEntities.ToDictionary(e => e.Name, e => e.GetComponent<PowerNodeComponent>());

        PowerNodeComponent Resolve(string name)
            => name != null && nodesByName.TryGetValue(name, out var n) ? n : null;

        // Level config.
        var discoveryNodes = new HashSet<string>();
        foreach (var e in entities)
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl == null) continue;
            _levelConfig = lvl;
            InitialInventory = new List<int>(lvl.Inventory);
            HoldingSlots = lvl.HoldingSlots;
            ActivationEdges = new List<(string, string)>(lvl.Activations);
            discoveryNodes = new HashSet<string>(lvl.DiscoveryNodes);
            break;
        }

        // Locks (resolve keys).
        var locks = new List<EdgeLockComponent>();
        foreach (var e in entities)
        {
            var lck = e.GetComponent<EdgeLockComponent>();
            if (lck == null) continue;
            lck.KeyNodeRef = Resolve(lck.KeyNode);
            locks.Add(lck);
        }

        // Connected components (union-find over connections, undirected).
        var parent = new Dictionary<string, string>();
        foreach (var e in nodeEntities) parent[e.Name] = e.Name;

        string Find(string x)
        {
            var root = x;
            while (parent[root] != root) root = parent[root];
            while (parent[x] != root) { var next = parent[x]; parent[x] = root; x = next; }
            return root;
        }
        void Union(string a, string b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        foreach (var e in nodeEntities)
            foreach (var t in nodesByName[e.Name].OutgoingNodeNames)
                if (nodesByName.ContainsKey(t)) Union(e.Name, t);

        // Group nodes by component root; component id = smallest node name.
        var comps = new Dictionary<string, List<PowerNodeComponent>>();
        foreach (var e in nodeEntities)
            (comps.TryGetValue(Find(e.Name), out var list) ? list : comps[Find(e.Name)] = new List<PowerNodeComponent>())
                .Add(nodesByName[e.Name]);

        var idByRoot = new Dictionary<string, string>();
        foreach (var (root, list) in comps)
        {
            var id = list.Select(n => n.Entity.Name).OrderBy(s => s, System.StringComparer.Ordinal).First();
            idByRoot[root] = id;
            foreach (var n in list) _componentOfNode[n.Entity.Name] = id;
        }

        // Attach each lock to a component: key node's component, else the nearest node's.
        var lockGroups = new Dictionary<string, List<EdgeLockComponent>>();
        foreach (var lck in locks)
        {
            string compId = lck.KeyNodeRef != null ? _componentOfNode.GetValueOrDefault(lck.KeyNodeRef.Entity.Name) : null;
            if (compId == null && nodesByName.Count > 0)
            {
                var mid = (lck.PointA + lck.PointB) * 0.5f;
                PowerNodeComponent nearest = null;
                float best = float.MaxValue;
                foreach (var n in nodesByName.Values)
                {
                    float d = Vector2.DistanceSquared(n.Entity.Position, mid);
                    if (d < best) { best = d; nearest = n; }
                }
                if (nearest != null) compId = _componentOfNode[nearest.Entity.Name];
            }
            if (compId == null) continue;
            (lockGroups.TryGetValue(compId, out var ll) ? ll : lockGroups[compId] = new List<EdgeLockComponent>()).Add(lck);
        }

        foreach (var (root, list) in comps)
        {
            var id = idByRoot[root];
            lockGroups.TryGetValue(id, out var groupLocks);
            var graph = new PuzzleGraph(list, groupLocks, Resolve) { Id = id };
            graph.DiscoveryEnabled = list.Any(n => discoveryNodes.Contains(n.Entity.Name));
            graph.TickCap = _levelConfig?.TickCapFor(id) ?? 64;
            graph.ResetRun();   // re-apply discovery baseline now that DiscoveryEnabled is known
            _graphs.Add(graph);
            _graphsById[id] = graph;
        }

        // Activation prerequisites by component (resolved from node-name edges).
        foreach (var (fromN, toN) in ActivationEdges)
        {
            var cf = _componentOfNode.GetValueOrDefault(fromN);
            var ct = _componentOfNode.GetValueOrDefault(toN);
            if (cf == null || ct == null || cf == ct) continue;
            (_prereqs.TryGetValue(ct, out var l) ? l : _prereqs[ct] = new List<string>()).Add(cf);
        }
    }

    public void Update(float deltaTime)
    {
        // v2: the simulation is driven explicitly (Run/Step/Reset), not every frame. Per-frame work
        // is just witness-style activation chaining, which reads each graph's last solved state.
        ApplyActivation();
    }

    /// <summary>Run every active puzzle's pulse simulation to completion (used by the Run control).</summary>
    public void RunAll()
    {
        foreach (var graph in _graphs)
            if (graph.IsActive)
                graph.RunToCompletion();
        ApplyActivation();
    }

    /// <summary>Begin a fresh run on every active puzzle without stepping it (used by single-step mode).</summary>
    public void StartAll()
    {
        foreach (var graph in _graphs)
            if (graph.IsActive)
                graph.StartRun();
        ApplyActivation();
    }

    /// <summary>Advance every running puzzle one tick (used by the Step control / auto-play timer).</summary>
    public bool StepAll()
    {
        bool any = false;
        foreach (var graph in _graphs)
            if (graph.IsRunning)
                any |= graph.StepTick();
        ApplyActivation();
        return any;
    }

    /// <summary>Clear run state on every puzzle back to the placement phase.</summary>
    public void ResetAllRuns()
    {
        foreach (var graph in _graphs)
            graph.ResetRun();
        ApplyActivation();
    }

    /// <summary>
    /// Witness-style chaining: a puzzle is active iff all its prerequisite puzzles are active *and*
    /// solved. No-prereq puzzles are always active. Computed to a fixpoint so chains enable in order
    /// and deactivate downstream when an upstream puzzle is unsolved.
    /// </summary>
    private void ApplyActivation()
    {
        if (_prereqs.Count == 0) return;

        var active = new Dictionary<string, bool>();
        foreach (var g in _graphs)
            active[g.Id] = !_prereqs.TryGetValue(g.Id, out var pr) || pr.Count == 0;

        bool changed = true;
        for (int guard = 0; changed && guard < 64; guard++)
        {
            changed = false;
            foreach (var g in _graphs)
            {
                bool na = !_prereqs.TryGetValue(g.Id, out var pr) || pr.Count == 0
                    || pr.All(p => active.TryGetValue(p, out var pa) && pa
                                   && _graphsById.TryGetValue(p, out var pg) && pg.IsSolved);
                if (na != active[g.Id]) { active[g.Id] = na; changed = true; }
            }
        }

        foreach (var g in _graphs)
            g.IsActive = active[g.Id];
    }
}
