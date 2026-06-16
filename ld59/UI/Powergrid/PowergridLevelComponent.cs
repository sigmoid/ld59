using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Components;

namespace ld59.UI.Powergrid;

/// <summary>
/// Level-wide configuration, authored on a single config entity. Defines the fixed inventory of
/// power tokens, the number of temporary holding slots, and the witness-style activation links
/// between sub-puzzles. All of these are collections, so they ride in the component Data blob
/// (the auto-serializer can't do collections).
///
/// Data format: ';'-separated entries of "key:value":
///   inventory:1,1,2          fixed starting token powers
///   activation:p0&gt;p1,p1&gt;p2   "p0 enables p1", "p1 enables p2"
/// </summary>
public class PowergridLevelComponent : Component
{
    public int HoldingSlots { get; set; } = 3;

    /// <summary>Power values of the fixed starting tokens (e.g. 1,1,2).</summary>
    public List<int> Inventory = new();

    /// <summary>Activation edges (from,to): solving puzzle <c>from</c> enables puzzle <c>to</c>.</summary>
    public List<(string From, string To)> Activations = new();

    /// <summary>Names of nodes whose puzzle has the discovery/fog mechanic enabled. A puzzle is
    /// discovery-enabled if it contains any of these nodes. Empty = all nodes visible everywhere.</summary>
    public List<string> DiscoveryNodes = new();

    /// <summary>Default pulse-simulation tick cap applied to puzzles with no explicit override.</summary>
    public int DefaultTickCap { get; set; } = 64;

    /// <summary>Per-puzzle tick-cap overrides, keyed by puzzle id (the smallest node name in the
    /// connected component). Editable per puzzle; puzzles absent here use <see cref="DefaultTickCap"/>.</summary>
    public Dictionary<string, int> TickCaps = new();

    /// <summary>Tick cap for a given puzzle id, falling back to the level default.</summary>
    public int TickCapFor(string puzzleId)
        => puzzleId != null && TickCaps.TryGetValue(puzzleId, out var cap) ? cap : DefaultTickCap;

    public override string SerializeData()
    {
        var parts = new List<string>();
        if (Inventory.Count > 0)
            parts.Add("inventory:" + string.Join(",", Inventory));
        if (Activations.Count > 0)
            parts.Add("activation:" + string.Join(",", Activations.Select(a => $"{a.From}>{a.To}")));
        if (DiscoveryNodes.Count > 0)
            parts.Add("discovery:" + string.Join(",", DiscoveryNodes));
        if (TickCaps.Count > 0)
            parts.Add("tickcaps:" + string.Join(",", TickCaps.Select(kv => $"{kv.Key}={kv.Value}")));
        return string.Join(";", parts);
    }

    public override void DeserializeData(string data)
    {
        Inventory.Clear();
        Activations.Clear();
        DiscoveryNodes.Clear();
        TickCaps.Clear();
        if (string.IsNullOrWhiteSpace(data)) return;

        foreach (var entry in data.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':');
            var key = colon >= 0 ? entry[..colon].Trim() : entry.Trim();
            var value = colon >= 0 ? entry[(colon + 1)..].Trim() : entry.Trim();

            switch (key)
            {
                case "activation":
                    foreach (var edge in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var gt = edge.Split('>', 2);
                        if (gt.Length == 2) Activations.Add((gt[0].Trim(), gt[1].Trim()));
                    }
                    break;

                case "discovery":
                    foreach (var n in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        DiscoveryNodes.Add(n);
                    break;

                case "tickcaps":
                    foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var eq = pair.Split('=', 2);
                        if (eq.Length == 2 && int.TryParse(eq[1].Trim(), out var cap))
                            TickCaps[eq[0].Trim()] = cap;
                    }
                    break;

                // "inventory" — or a bare comma list with no key (back-compat).
                default:
                    foreach (var s in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (int.TryParse(s, out var p)) Inventory.Add(p);
                    break;
            }
        }
    }
}
