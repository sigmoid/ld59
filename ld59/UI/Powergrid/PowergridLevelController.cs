using System.Collections.Generic;
using System.Linq;
using Quartz;

namespace ld59.UI.Powergrid;

/// <summary>
/// Owns the loaded <see cref="Scene"/> for a Powergrid level and bridges the serialized
/// component data to the runtime solver. On construction it runs the post-load resolve pass
/// (turning by-name references into live component references, since entity Guids are regenerated
/// each load), groups nodes/locks into sub-puzzles, and builds a <see cref="PuzzleGraph"/> per group.
/// Reconstructed each session; never serialized.
/// </summary>
public class PowergridLevelController
{
    private const string DefaultPuzzleId = "default";
    private const string PuzzleTagPrefix = "puzzle:";

    private readonly Scene _scene;
    private readonly List<PuzzleGraph> _graphs = new();
    private readonly Dictionary<string, PuzzleGraph> _graphsById = new();

    // Activation chaining: prereqs[X] = puzzles that must be solved before X is active.
    private readonly Dictionary<string, List<string>> _prereqs = new();

    public Scene Scene => _scene;
    public IReadOnlyList<PuzzleGraph> Graphs => _graphs;

    /// <summary>Fixed starting inventory (token powers) declared by the level config entity, if any.</summary>
    public IReadOnlyList<int> InitialInventory { get; private set; } = new List<int>();
    public int HoldingSlots { get; private set; } = 3;

    public PuzzleGraph GraphOf(PowerNodeComponent node)
        => _graphs.FirstOrDefault(g => g.Nodes.Contains(node));

    public PowergridLevelController(Scene scene)
    {
        _scene = scene;
        Build();
    }

    private void Build()
    {
        var entities = _scene.GetEntities();

        // Name -> node map for reference resolution (names are unique within a scene).
        var nodesByName = new Dictionary<string, PowerNodeComponent>();
        foreach (var e in entities)
        {
            var node = e.GetComponent<PowerNodeComponent>();
            if (node != null) nodesByName[e.Name] = node;
        }

        PowerNodeComponent Resolve(string name)
            => name != null && nodesByName.TryGetValue(name, out var n) ? n : null;

        // Level config (inventory / holding slots / activation links), if a config entity is present.
        foreach (var e in entities)
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl == null) continue;
            InitialInventory = new List<int>(lvl.Inventory);
            HoldingSlots = lvl.HoldingSlots;
            foreach (var (from, to) in lvl.Activations)
                (_prereqs.TryGetValue(to, out var list) ? list : _prereqs[to] = new List<string>()).Add(from);
            break;
        }

        // Resolve lock key references.
        var locks = new List<EdgeLockComponent>();
        foreach (var e in entities)
        {
            var lck = e.GetComponent<EdgeLockComponent>();
            if (lck == null) continue;
            lck.KeyNodeRef = Resolve(lck.KeyNode);
            locks.Add(lck);
        }

        // Group nodes and locks by sub-puzzle id (entity Tag "puzzle:<id>"; untagged -> default).
        var nodeGroups = new Dictionary<string, List<PowerNodeComponent>>();
        foreach (var e in entities)
        {
            var node = e.GetComponent<PowerNodeComponent>();
            if (node == null) continue;
            var id = PuzzleIdOf(e.Tag);
            (nodeGroups.TryGetValue(id, out var list) ? list : nodeGroups[id] = new List<PowerNodeComponent>()).Add(node);
        }

        var lockGroups = new Dictionary<string, List<EdgeLockComponent>>();
        foreach (var lck in locks)
        {
            var id = PuzzleIdOf(lck.Entity.Tag);
            (lockGroups.TryGetValue(id, out var list) ? list : lockGroups[id] = new List<EdgeLockComponent>()).Add(lck);
        }

        foreach (var (id, nodes) in nodeGroups)
        {
            lockGroups.TryGetValue(id, out var groupLocks);
            var graph = new PuzzleGraph(nodes, groupLocks, Resolve) { Id = id };
            _graphs.Add(graph);
            _graphsById[id] = graph;
        }
    }

    private static string PuzzleIdOf(string tag)
        => !string.IsNullOrEmpty(tag) && tag.StartsWith(PuzzleTagPrefix)
            ? tag.Substring(PuzzleTagPrefix.Length)
            : DefaultPuzzleId;

    public void Update(float deltaTime)
    {
        // Compute power / solved / discovery for every puzzle (cheap; idempotent for inactive ones).
        foreach (var graph in _graphs)
            graph.Update();

        ApplyActivation();
    }

    /// <summary>
    /// Witness-style chaining: a puzzle is active iff all its prerequisite puzzles are themselves
    /// active *and* solved. Puzzles with no prerequisites are always active. Computed to a fixpoint
    /// so a chain enables in order and deactivates downstream when an upstream puzzle is unsolved.
    /// </summary>
    private void ApplyActivation()
    {
        if (_prereqs.Count == 0) return; // single-puzzle / no chaining: leave all active

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
