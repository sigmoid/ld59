using System.Collections.Generic;
using System.Linq;
using Quartz;

namespace ld59.UI.Powergrid;

/// <summary>
/// Owns the loaded <see cref="Scene"/> for a graph-colouring level and bridges the serialized
/// component data to the runtime model. Reconstructed each session; never serialized.
///
/// Sub-puzzles are determined automatically: each connected component of the node graph (treating
/// connections as undirected) is one puzzle. A component's id is the smallest node name in it.
/// "Filling the whole graph" means solving every component.
/// </summary>
public class PowergridLevelController
{
    private readonly Scene _scene;
    private readonly List<PuzzleGraph> _graphs = new();
    private readonly List<PowergridRegionComponent> _regions = new();
    private readonly List<PowergridTextComponent> _labels = new();

    public Scene Scene => _scene;
    public IReadOnlyList<PuzzleGraph> Graphs => _graphs;

    /// <summary>Decorative text labels authored in this level (no effect on solving).</summary>
    public IReadOnlyList<PowergridTextComponent> Labels => _labels;

    /// <summary>All tier-count regions authored in this level. Each region limits how many runes of a
    /// specific tier may be placed within its bounding box.</summary>
    public IReadOnlyList<PowergridRegionComponent> Regions => _regions;

    /// <summary>The player's starting rune inventory (a flat list; repeats = quantity), from the
    /// level config. Empty if the level authors none.</summary>
    public IReadOnlyList<string> InitialInventory { get; private set; } = new List<string>();

    /// <summary>Active adjacency rules (from the level config, default "different rune").</summary>
    public IReadOnlyList<ColoringRule> ActiveRules { get; private set; } = new[] { ColoringRule.DifferentRune };

    private PowergridLevelComponent _levelConfig;
    public PowergridLevelComponent LevelConfig => _levelConfig;

    public PuzzleGraph GraphOf(PowerNodeComponent node) => _graphs.FirstOrDefault(g => g.Nodes.Contains(node));

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

        // Collect all region + label components.
        _regions.Clear();
        _labels.Clear();
        foreach (var e in entities)
        {
            var region = e.GetComponent<PowergridRegionComponent>();
            if (region != null) _regions.Add(region);

            var label = e.GetComponent<PowergridTextComponent>();
            if (label != null) _labels.Add(label);
        }

        PowerNodeComponent Resolve(string name)
            => name != null && nodesByName.TryGetValue(name, out var n) ? n : null;

        // Level config (palette size).
        foreach (var e in entities)
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl == null) continue;
            _levelConfig = lvl;
            InitialInventory = new List<string>(lvl.Inventory);
            if (lvl.Rules.Count > 0) ActiveRules = new List<ColoringRule>(lvl.Rules);
            break;
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

        var comps = new Dictionary<string, List<PowerNodeComponent>>();
        foreach (var e in nodeEntities)
            (comps.TryGetValue(Find(e.Name), out var list) ? list : comps[Find(e.Name)] = new List<PowerNodeComponent>())
                .Add(nodesByName[e.Name]);

        foreach (var (_, list) in comps)
        {
            var id = list.Select(n => n.Entity.Name).OrderBy(s => s, System.StringComparer.Ordinal).First();
            _graphs.Add(new PuzzleGraph(list, Resolve)
            {
                Id = id,
                Rules = ActiveRules,
                Order = _levelConfig?.OrderOf(id) ?? 0,
                RewardRunes = _levelConfig?.RewardOf(id) ?? System.Array.Empty<string>(),
            });
        }

        // Sequence order: authored index first, id as a stable tie-break.
        _graphs.Sort((a, b) =>
        {
            int byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : System.StringComparer.Ordinal.Compare(a.Id, b.Id);
        });
    }

    /// <summary>Recompute conflicts/solved state on every puzzle, then the sequence gating: a puzzle is
    /// unlocked only once every earlier puzzle in the sequence is currently solved.</summary>
    public void Update(float deltaTime)
    {
        foreach (var graph in _graphs) graph.Recompute();

        bool prevAllSolved = true;
        foreach (var graph in _graphs)
        {
            graph.Unlocked = prevAllSolved;
            prevAllSolved = prevAllSolved && graph.IsSolved;
        }

        EvaluateRegions();
    }

    /// <summary>Recomputes each region's current rune count and violation flag from the live node
    /// states. A region is violated when its tier-rune count exceeds <see cref="PowergridRegionComponent.MaxCount"/>.</summary>
    public void EvaluateRegions()
    {
        if (_regions.Count == 0) return;

        foreach (var region in _regions)
        {
            int count = 0;
            foreach (var graph in _graphs)
                foreach (var node in graph.Nodes)
                    if (node.HasRune && region.Contains(node.Entity.Position))
                    {
                        var sym = Runes.ByName(node.Rune);
                        if (sym != null && sym.Tier == region.Tier) count++;
                    }
            region.CurrentCount = count;
            region.IsViolated = count > region.MaxCount;
        }
    }

    /// <summary>Clear all player-placed runes (fixed clues kept) across every puzzle.</summary>
    public void ClearPlaced()
    {
        foreach (var graph in _graphs) graph.ClearPlaced();
    }

    /// <summary>True once every puzzle in the level is solved and all tier-count regions are satisfied.</summary>
    public bool IsLevelSolved => _graphs.Count > 0 && _graphs.All(g => g.IsSolved) && !_regions.Any(r => r.IsViolated);
}
