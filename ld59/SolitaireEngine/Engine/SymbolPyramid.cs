using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// A single symbol in the pyramid, located by its level (0 = apex) and index within
// that level. Levels widen toward the bottom, e.g. {1,2,3,5,7}:
//
//          (0,0)              level 0
//        (1,0)(1,1)           level 1
//      (2,0)(2,1)(2,2)        level 2
//   (3,0)(3,1)(3,2)(3,3)(3,4) level 3
// (4,0) ...                (4,6) level 4
public sealed class PyramidNode<T>
{
    public int Level { get; }   // 0 = apex (top), increasing downward
    public int Index { get; }   // position within the level, left to right
    public T   Symbol { get; internal set; }

    public bool IsRemoved { get; internal set; }

    internal PyramidNode(int level, int index, T symbol)
    {
        Level  = level;
        Index  = index;
        Symbol = symbol;
    }

    public override string ToString() => $"L{Level}[{Index}]={Symbol}";
}

// Models a pyramid of symbols arranged in levels of arbitrary width and the
// whole-level tier relationship between them: a symbol is "free" (playable) only
// once the entire level directly below it has been cleared. The bottom (widest)
// level is free from the start; the apex is reached last.
public sealed class SymbolPyramid<T>
{
    private readonly List<PyramidNode<T>[]> _levels = new();
    private readonly List<PyramidNode<T>>   _all    = new();

    public IReadOnlyList<PyramidNode<T>> Nodes => _all;
    public int LevelCount => _levels.Count;
    public int Count       => _all.Count;

    public IReadOnlyList<PyramidNode<T>> Level(int level) => _levels[level];

    public PyramidNode<T> At(int level, int index)
        => level >= 0 && level < _levels.Count && index >= 0 && index < _levels[level].Length
            ? _levels[level][index]
            : null;

    // Build from explicit level widths (top to bottom), e.g. {1,2,3,5,7} for 18
    // symbols. Symbols are filled left-to-right, top-to-bottom.
    public static SymbolPyramid<T> Build(IReadOnlyList<int> levelSizes, IReadOnlyList<T> symbols)
    {
        if (levelSizes == null || levelSizes.Count == 0) throw new ArgumentException("Need at least one level.", nameof(levelSizes));
        int expected = levelSizes.Sum();
        if (symbols == null || symbols.Count != expected)
            throw new ArgumentException($"Level sizes total {expected} cells but got {symbols?.Count ?? 0} symbols.", nameof(symbols));

        var pyramid = new SymbolPyramid<T>();
        int s = 0;
        for (int lvl = 0; lvl < levelSizes.Count; lvl++)
        {
            var level = new PyramidNode<T>[levelSizes[lvl]];
            for (int i = 0; i < level.Length; i++)
            {
                var node = new PyramidNode<T>(lvl, i, symbols[s++]);
                level[i] = node;
                pyramid._all.Add(node);
            }
            pyramid._levels.Add(level);
        }
        return pyramid;
    }

    // Convenience for a regular pyramid: rows of 1,2,3,…,n (top to bottom).
    // Requires a triangular count (1,3,6,10,15,21,…); throws otherwise with the
    // nearest valid sizes. 15 symbols -> 5 rows of {1,2,3,4,5}.
    public static SymbolPyramid<T> Triangular(IReadOnlyList<T> symbols)
    {
        int count = symbols?.Count ?? 0;
        int n = (int)((System.Math.Sqrt(8.0 * count + 1) - 1) / 2);
        if (n * (n + 1) / 2 != count)
            throw new ArgumentException(
                $"{count} symbols do not form a complete pyramid. " +
                $"Nearest complete sizes: {n * (n + 1) / 2} ({n} rows) or {(n + 1) * (n + 2) / 2} ({n + 1} rows). " +
                $"Use Build(...) with explicit row sizes for an irregular pyramid.",
                nameof(symbols));

        return Build(Enumerable.Range(1, n).ToArray(), symbols);
    }

    // ── The tier relationship ───────────────────────────────────────────────────

    public bool IsLevelCleared(int level) => _levels[level].All(n => n.IsRemoved);

    // A symbol is exposed once the level directly below it is fully cleared.
    // The bottom level has nothing below it, so it is exposed from the start.
    public bool IsExposed(PyramidNode<T> node)
        => node.Level == _levels.Count - 1 || IsLevelCleared(node.Level + 1);

    public bool IsFree(PyramidNode<T> node) => !node.IsRemoved && IsExposed(node);

    // Index of the deepest level that still has symbols — the only level currently
    // in play. Returns -1 when the pyramid is fully cleared.
    public int ActiveLevel()
    {
        for (int lvl = _levels.Count - 1; lvl >= 0; lvl--)
            if (_levels[lvl].Any(n => !n.IsRemoved))
                return lvl;
        return -1;
    }

    // Every symbol currently playable: the remaining symbols on the active level.
    public IEnumerable<PyramidNode<T>> ExposedNodes()
    {
        int lvl = ActiveLevel();
        return lvl < 0 ? Enumerable.Empty<PyramidNode<T>>()
                       : _levels[lvl].Where(n => !n.IsRemoved);
    }

    public bool IsSolved => _all.All(n => n.IsRemoved);

    public void Remove(PyramidNode<T> node) => node.IsRemoved = true;

    public void Reset()
    {
        foreach (var n in _all) n.IsRemoved = false;
    }

    // Centered ASCII view — handy while experimenting with layouts.
    public string Render(Func<PyramidNode<T>, string> cell = null)
    {
        cell ??= n => n.IsRemoved ? "·" : n.Symbol.ToString();
        int width  = _all.Max(n => cell(n).Length) + 1;
        int maxLvl = _levels.Max(l => l.Length);
        var sb = new StringBuilder();
        foreach (var level in _levels)
        {
            int pad = (maxLvl - level.Length) * width / 2;
            sb.Append(new string(' ', pad));
            foreach (var node in level)
                sb.Append(cell(node).PadLeft(width));
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
