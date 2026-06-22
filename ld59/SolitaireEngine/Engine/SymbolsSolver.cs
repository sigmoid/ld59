using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

// Brute-force solver for the Symbols mode. It explores every reachable state via single-card
// moves (column tops and free cells), deduplicating equivalent states so the search is finite,
// and reports whether the deal can be solved.
//
// "Solved" = every free cell empty and every column either empty or a complete run: a full
// descending tier-(max)..1 sequence with alternating suits. With the equal-tier deck the whole
// pack is exactly N complete runs, so this is reachable only when there are enough columns.
//
// A card is encoded as a byte: (tier << 1) | suit, where suit 0 = Light, 1 = Dark. The stacking
// test comes from SymbolStackRule so it always matches the live game rules.
public sealed class SymbolsSolver
{
    public sealed class Problem
    {
        public List<List<byte>> Columns = new();   // bottom .. top, per column
        public List<byte>       FreeCells = new();  // currently occupied free cells
        public int ColumnCount;
        public int FreeCellCapacity;
        public int MaxTier;
    }

    public enum Outcome { Winnable, Unwinnable, SearchLimitReached }

    // A single-card move expressed against the *active* columns (the order the Problem was built in,
    // i.e. the live game's non-completed columns). A -1 index means the free cells: as a source it
    // refers to the cell holding Card; as a destination it means any open cell.
    public readonly struct Move
    {
        public readonly int  FromColumn;
        public readonly int  ToColumn;
        public readonly byte Card;
        public Move(int fromColumn, int toColumn, byte card) { FromColumn = fromColumn; ToColumn = toColumn; Card = card; }
    }

    public sealed class Result
    {
        public Outcome    Outcome;
        public int        StatesExplored;
        public long       ElapsedMs;
        public List<Move> Moves = new();

        public override string ToString() => Outcome switch
        {
            Outcome.Winnable           => $"WINNABLE in {Moves.Count} moves ({StatesExplored} states, {ElapsedMs} ms)",
            Outcome.Unwinnable         => $"NOT winnable (searched {StatesExplored} states, {ElapsedMs} ms)",
            _                          => $"UNKNOWN - hit {StatesExplored}-state limit ({ElapsedMs} ms)",
        };
    }

    private static int  Tier(byte code) => code >> 1;
    private static int  Suit(byte code) => code & 1;
    private static bool IsRunStep(byte lower, byte upper) => SymbolStackRule.IsRunStep(Tier(lower), Suit(lower), Tier(upper), Suit(upper));

    // Greedy best-first search: always expand the state that looks closest to solved (fewest
    // out-of-place cards). A time/state budget guarantees it returns promptly even when a deal is
    // unsolvable or just hard — in that case the outcome is SearchLimitReached ("unknown").
    public static Result Solve(Problem problem, int maxStates = 3_000_000, int timeBudgetMs = 15_000)
    {
        var sw = Stopwatch.StartNew();

        var cols = problem.Columns.Select(c => new List<byte>(c)).ToList();
        while (cols.Count < problem.ColumnCount) cols.Add(new List<byte>());
        var free = new List<byte>(problem.FreeCells);

        int total = cols.Sum(c => c.Count) + free.Count;

        // meta doubles as the visited set: a key is present iff that state has been discovered.
        var meta     = new Dictionary<string, (string parent, Move move)>();
        var startKey = Key(cols, free);
        meta[startKey] = (null, default);

        // Lowest heuristic is expanded first.
        var open = new PriorityQueue<(List<List<byte>> cols, List<byte> free, string key), int>();
        open.Enqueue((cols, free, startKey), Heuristic(cols, free, problem.MaxTier, total));

        int explored = 0;
        while (open.Count > 0)
        {
            var (c, f, key) = open.Dequeue();
            explored++;

            if (IsWin(c, f, problem.MaxTier))
            {
                sw.Stop();
                return new Result { Outcome = Outcome.Winnable, StatesExplored = explored, ElapsedMs = sw.ElapsedMilliseconds, Moves = Reconstruct(meta, key) };
            }

            if (meta.Count >= maxStates || (explored & 0x3FF) == 0 && sw.ElapsedMilliseconds >= timeBudgetMs)
            {
                sw.Stop();
                return new Result { Outcome = Outcome.SearchLimitReached, StatesExplored = explored, ElapsedMs = sw.ElapsedMilliseconds };
            }

            foreach (var (nc, nf, move, _) in Successors(c, f, problem.FreeCellCapacity, problem.MaxTier))
            {
                var nkey = Key(nc, nf);
                if (!meta.ContainsKey(nkey))
                {
                    meta[nkey] = (key, move);
                    open.Enqueue((nc, nf, nkey), Heuristic(nc, nf, problem.MaxTier, total));
                }
            }
        }

        sw.Stop();
        return new Result { Outcome = Outcome.Unwinnable, StatesExplored = explored, ElapsedMs = sw.ElapsedMilliseconds };
    }

    // Number of cards not yet settled into a valid run growing up from a tier-max base. 0 = solved.
    private static int Heuristic(List<List<byte>> cols, List<byte> free, int maxTier, int total)
    {
        int settled = 0;
        foreach (var col in cols)
        {
            if (col.Count == 0 || Tier(col[0]) != maxTier) continue;  // only a tier-max base anchors a run
            settled++;
            for (int i = 1; i < col.Count && IsRunStep(col[i - 1], col[i]); i++)
                settled++;
        }
        return total - settled;
    }

    private static IEnumerable<(List<List<byte>> cols, List<byte> free, Move move, int prio)> Successors(
        List<List<byte>> cols, List<byte> free, int freeCapacity, int maxTier)
    {
        int  emptyCol  = cols.FindIndex(c => c.Count == 0);   // one representative empty column
        bool freeAvail = free.Count < freeCapacity;

        // Move the top card of each column.
        for (int i = 0; i < cols.Count; i++)
        {
            if (cols[i].Count == 0) continue;
            if (IsCompleteRun(cols[i], maxTier)) continue;   // a finished run is locked (removed in-game)
            byte card = cols[i][^1];
            int  col  = i;

            foreach (var s in Destinations(cols, free, card, srcColumn: col, emptyCol, freeAvail, maxTier, srcColIsSingle: cols[i].Count == 1,
                         remove: (c, f) => c[col].RemoveAt(c[col].Count - 1)))
                yield return s;
        }

        // Move a card out of a free cell.
        for (int fi = 0; fi < free.Count; fi++)
        {
            byte card = free[fi];
            int  idx  = fi;
            foreach (var s in Destinations(cols, free, card, srcColumn: -1, emptyCol, freeAvail: false, maxTier, srcColIsSingle: false,
                         remove: (c, f) => f.RemoveAt(idx)))
                yield return s;
        }
    }

    private static IEnumerable<(List<List<byte>> cols, List<byte> free, Move move, int prio)> Destinations(
        List<List<byte>> cols, List<byte> free, byte card, int srcColumn,
        int emptyCol, bool freeAvail, int maxTier, bool srcColIsSingle, Action<List<List<byte>>, List<byte>> remove)
    {
        bool srcIsCell = srcColumn < 0;

        // Onto another column.
        for (int j = 0; j < cols.Count; j++)
        {
            if (cols[j].Count > 0 && IsCompleteRun(cols[j], maxTier)) continue;   // can't add to a locked run

            bool empty = cols[j].Count == 0;
            int  prio;
            if (empty)
            {
                if (j != emptyCol) continue;       // all empty columns are equivalent
                if (srcColIsSingle) continue;      // relocating a lone card is a no-op
                prio = 5;                          // parking on an empty column: low priority
            }
            else
            {
                byte top          = cols[j][^1];
                int  belowTopTier = cols[j].Count >= 2 ? Tier(cols[j][^2]) : -1;
                if (!SymbolStackRule.CanPlaceOn(Tier(top), Suit(top), belowTopTier, Tier(card), Suit(card), isSingle: true))
                    continue;

                if (IsRunStep(top, card))
                    // Extending a real run is good; unloading a free cell onto a run is best. Taller
                    // targets (closer to a complete run) rank higher.
                    prio = (srcIsCell ? 100 : 50) + cols[j].Count;
                else
                    // Same-tier stacking is just parking — only mildly better than an empty column.
                    prio = srcIsCell ? 20 : 8;
            }

            var (nc, nf) = Clone(cols, free);
            remove(nc, nf);
            nc[j].Add(card);
            yield return (nc, nf, new Move(srcColumn, j, card), prio);
        }

        // Into a free cell (last resort).
        if (freeAvail)
        {
            var (nc, nf) = Clone(cols, free);
            remove(nc, nf);
            nf.Add(card);
            yield return (nc, nf, new Move(srcColumn, -1, card), 1);
        }
    }

    private static (List<List<byte>>, List<byte>) Clone(List<List<byte>> cols, List<byte> free)
    {
        var nc = new List<List<byte>>(cols.Count);
        foreach (var c in cols) nc.Add(new List<byte>(c));
        return (nc, new List<byte>(free));
    }

    private static bool IsWin(List<List<byte>> cols, List<byte> free, int maxTier)
    {
        if (free.Count > 0) return false;
        foreach (var col in cols)
            if (col.Count != 0 && !IsCompleteRun(col, maxTier)) return false;
        return true;
    }

    // A column holding exactly a full tier-max..1 alternating-suit run. The game removes these, and
    // the solver treats them as locked so it never relies on dismantling a finished run.
    private static bool IsCompleteRun(List<byte> col, int maxTier)
    {
        if (col.Count != maxTier || Tier(col[0]) != maxTier) return false;
        for (int i = 0; i < col.Count - 1; i++)
            if (!IsRunStep(col[i], col[i + 1])) return false;
        return true;
    }

    private static string Key(List<List<byte>> cols, List<byte> free)
    {
        // Columns and free cells are interchangeable, so canonicalize by sorting.
        var colStrs = new List<string>(cols.Count);
        foreach (var col in cols)
        {
            if (col.Count == 0) continue;
            var arr = new char[col.Count];
            for (int i = 0; i < col.Count; i++) arr[i] = (char)(col[i] + 1);
            colStrs.Add(new string(arr));
        }
        colStrs.Sort(StringComparer.Ordinal);

        var freeChars = new char[free.Count];
        for (int i = 0; i < free.Count; i++) freeChars[i] = (char)(free[i] + 1);
        Array.Sort(freeChars);

        return string.Join("/", colStrs) + "|" + new string(freeChars);
    }

    private static List<Move> Reconstruct(Dictionary<string, (string parent, Move move)> meta, string winKey)
    {
        var moves = new List<Move>();
        var key   = winKey;
        while (key != null && meta.TryGetValue(key, out var m) && m.parent != null)
        {
            moves.Add(m.move);
            key = m.parent;
        }
        moves.Reverse();
        return moves;
    }
}
