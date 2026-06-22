using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

namespace ld59.UI.Powergrid;

/// <summary>
/// Central access to the rune set used by the graph-colouring puzzles. A "rune" is one of the
/// game's <see cref="Symbol"/>s, referenced by its <see cref="Symbol.Name"/>. A level's palette is
/// the first N symbols from <see cref="SymbolDictionary.All"/>; N is authored per level.
/// </summary>
public static class Runes
{
    public const int DefaultPaletteSize = 4;
    public static int MaxPaletteSize => SymbolDictionary.All.Count;

    private static readonly Dictionary<string, Texture2D> _textures = new();

    /// <summary>The first <paramref name="size"/> symbols (clamped to the available range).</summary>
    public static IReadOnlyList<Symbol> Palette(int size)
        => SymbolDictionary.All.Take(System.Math.Clamp(size, 1, MaxPaletteSize)).ToList();

    public static Symbol ByName(string name)
        => name == null ? null : SymbolDictionary.All.FirstOrDefault(s => s.Name == name);

    /// <summary>Loads (and caches) the texture for a rune name, or null if unknown.</summary>
    public static Texture2D Texture(string runeName)
    {
        if (string.IsNullOrEmpty(runeName)) return null;
        if (_textures.TryGetValue(runeName, out var cached)) return cached;

        var symbol = ByName(runeName);
        Texture2D tex = null;
        if (symbol != null)
        {
            try { tex = Core.Content.Load<Texture2D>(Path.ChangeExtension(symbol.TexturePath, null)); }
            catch { tex = null; }
        }
        _textures[runeName] = tex;
        return tex;
    }
}
