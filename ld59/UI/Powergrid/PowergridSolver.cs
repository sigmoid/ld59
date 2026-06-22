using System.Collections.Generic;
using System.Linq;

namespace ld59.UI.Powergrid;

/// <summary>
/// Enumerates valid rune assignments for a graph-colouring level. Given the puzzles (with their
/// edges and fixed clues), the active adjacency rules, and a finite rune budget (a multiset), it
/// backtracks over every empty node and records each complete assignment that (a) never places more
/// copies of a rune than the budget holds and (b) violates no rule on any edge.
///
/// Edges only exist within a connected component, but the budget is shared across all of them, so the
/// whole scene is solved as one CSP. Distinct solutions are counted by rune <em>name</em> (identical
/// rune tokens are interchangeable), so swapping two equal runes is not a new solution.
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
        int maxSolutions = 1000)
    {
        var result = new Result();

        // Collect every node and build an undirected adjacency map.
        var adj = new Dictionary<PowerNodeComponent, List<PowerNodeComponent>>();
        foreach (var g in graphs)
        {
            foreach (var n in g.Nodes)
                if (!adj.ContainsKey(n)) adj[n] = new List<PowerNodeComponent>();
            foreach (var c in g.Connections)
            {
                adj[c.From].Add(c.To);
                adj[c.To].Add(c.From);
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
                foreach (var rule in rules)
                    if (ColoringRules.Violates(rule, rune, b)) return true;
            }
            return false;
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

                assign[node] = name;
                pool[name]--;
                Recurse(i + 1);
                pool[name]++;
                assign.Remove(node);

                if (result.Truncated) return;
            }
        }

        Recurse(0);
        return result;
    }
}
