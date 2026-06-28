using System.Collections.Generic;
using System.Linq;

namespace ld59.UI.Powergrid;

/// <summary>
/// Enumerates valid rune assignments for a graph-colouring level. Given the puzzles (with their
/// edges and fixed clues), the active adjacency rules, and a finite rune budget (a multiset), it
/// backtracks over every empty node and records each complete assignment that (a) never places more
/// copies of a rune than the budget holds and (b) violates no rule on any edge and (c) satisfies all
/// tier-count region constraints.
///
/// Edges only exist within a connected component, but the budget is shared across all of them, so the
/// whole scene is solved as one CSP. Distinct solutions are counted by rune <em>name</em> (identical
/// rune tokens are interchangeable), so swapping two equal runes is not a new solution.
///
/// Per-connection rule overrides are respected: each edge uses its own override if present, else the
/// level-wide rules. Region constraints are pruned eagerly — a candidate rune is skipped immediately
/// if placing it would push a region over its tier cap, rather than detecting the violation only at
/// the bottom of the tree.
/// </summary>
public static class PowergridSolver
{
    public sealed class Result
    {
        /// <summary>Each solution maps a (non-fixed) node to the rune name placed on it.</summary>
        public readonly List<Dictionary<PowerNodeComponent, string>> Solutions = new();

        /// <summary>True if the search hit the solution cap or the work budget before exhausting the
        /// space — the real count is "at least" <see cref="Solutions"/>.Count.</summary>
        public bool Truncated;
    }

    /// <param name="maxSolutions">Stop after collecting this many (keeps stepping/counting bounded).</param>
    public static Result Solve(
        IReadOnlyList<PuzzleGraph> graphs,
        IReadOnlyList<ColoringRule> rules,
        IReadOnlyList<string> budget,
        IReadOnlyList<PowergridRegionComponent> regions = null,
        int maxSolutions = 1000)
    {
        var result = new Result();

        // Collect every node, build an undirected adjacency map, and record per-edge effective rules.
        var adj = new Dictionary<PowerNodeComponent, List<PowerNodeComponent>>();
        var edgeRules = new Dictionary<(PowerNodeComponent, PowerNodeComponent), IReadOnlyList<ColoringRule>>();

        foreach (var g in graphs)
        {
            foreach (var n in g.Nodes)
                if (!adj.ContainsKey(n)) adj[n] = new List<PowerNodeComponent>();
            foreach (var c in g.Connections)
            {
                adj[c.From].Add(c.To);
                adj[c.To].Add(c.From);
                var effective = (IReadOnlyList<ColoringRule>)(c.RuleOverride ?? rules);
                edgeRules[(c.From, c.To)] = effective;
                edgeRules[(c.To, c.From)] = effective;
            }
        }

        // Nodes we must fill, most-constrained first (helps prune fast).
        var toAssign = adj.Keys.Where(n => !n.IsFixed).OrderByDescending(n => adj[n].Count).ToList();

        // Budget as per-rune counts (only runes that actually exist).
        var pool = new Dictionary<string, int>();
        foreach (var r in budget)
            if (Runes.ByName(r) != null)
                pool[r] = pool.TryGetValue(r, out var c) ? c + 1 : 1;
        var runeNames = pool.Keys.OrderBy(n => SymbolDictionary.All.FindIndex(s => s.Name == n)).ToList();

        // Pre-compute which nodes are inside each region (including fixed nodes), so the region
        // checks inside the hot loop don't have to call Contains() on every node every time.
        // regionBaseCounts[i] = number of fixed-rune nodes already inside region i with the right tier.
        int regionCount = regions?.Count ?? 0;
        var regionNodes = new List<PowerNodeComponent>[regionCount];      // non-fixed nodes inside each region
        var regionBaseCounts = new int[regionCount];                      // fixed contribution per region
        for (int ri = 0; ri < regionCount; ri++)
        {
            var region = regions[ri];
            regionNodes[ri] = new List<PowerNodeComponent>();
            foreach (var n in adj.Keys)
            {
                if (!region.Contains(n.Entity.Position)) continue;
                if (n.IsFixed)
                {
                    var sym = Runes.ByName(n.FixedRune);
                    if (sym != null && sym.Tier == region.Tier) regionBaseCounts[ri]++;
                }
                else
                {
                    regionNodes[ri].Add(n);
                }
            }
        }

        // Live tier-count per region, maintained incrementally during backtracking.
        var regionLiveCounts = new int[regionCount];
        for (int ri = 0; ri < regionCount; ri++) regionLiveCounts[ri] = regionBaseCounts[ri];

        var assign = new Dictionary<PowerNodeComponent, string>();
        long steps = 0;
        const long StepBudget = 4_000_000;

        bool Conflicts(PowerNodeComponent node, Symbol rune)
        {
            foreach (var nb in adj[node])
            {
                string other = nb.IsFixed ? nb.FixedRune : (assign.TryGetValue(nb, out var v) ? v : null);
                if (string.IsNullOrEmpty(other)) continue;
                var b = Runes.ByName(other);
                if (b == null) continue;
                var effective = edgeRules.TryGetValue((node, nb), out var er) ? er : rules;
                foreach (var rule in effective)
                    if (ColoringRules.Violates(rule, rune, b)) return true;
            }
            return false;
        }

        // Returns the index of the first region this placement would push over its cap, or -1 if none.
        // Only checks regions whose tier matches the rune being placed and that contain the node.
        int ViolatedRegion(PowerNodeComponent node, Symbol sym)
        {
            for (int ri = 0; ri < regionCount; ri++)
            {
                if (regions[ri].Tier != sym.Tier) continue;
                if (!regions[ri].Contains(node.Entity.Position)) continue;
                if (regionLiveCounts[ri] + 1 > regions[ri].MaxCount) return ri;
            }
            return -1;
        }

        void Recurse(int i)
        {
            if (result.Truncated) return;
            if (++steps > StepBudget) { result.Truncated = true; return; }

            if (i == toAssign.Count)
            {
                result.Solutions.Add(new Dictionary<PowerNodeComponent, string>(assign));
                if (result.Solutions.Count >= maxSolutions) result.Truncated = true;
                return;
            }

            var node = toAssign[i];
            foreach (var name in runeNames)
            {
                if (pool[name] <= 0) continue;
                var sym = Runes.ByName(name);
                if (sym == null || Conflicts(node, sym)) continue;

                // Eagerly prune if this placement pushes any region over its tier cap.
                if (regionCount > 0 && ViolatedRegion(node, sym) >= 0) continue;

                // Update live region counts before recursing.
                for (int ri = 0; ri < regionCount; ri++)
                    if (regions[ri].Tier == sym.Tier && regions[ri].Contains(node.Entity.Position))
                        regionLiveCounts[ri]++;

                assign[node] = name;
                pool[name]--;
                Recurse(i + 1);
                pool[name]++;
                assign.Remove(node);

                // Restore live region counts.
                for (int ri = 0; ri < regionCount; ri++)
                    if (regions[ri].Tier == sym.Tier && regions[ri].Contains(node.Entity.Position))
                        regionLiveCounts[ri]--;

                if (result.Truncated) return;
            }
        }

        Recurse(0);
        return result;
    }
}
