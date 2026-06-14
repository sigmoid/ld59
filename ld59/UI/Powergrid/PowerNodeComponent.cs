using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Components;

namespace ld59.UI.Powergrid;

public enum NodeKind
{
    Normal,
    And,
    Xor,
}

/// <summary>
/// Data for a single power node in a Powergrid puzzle. Lives on an <see cref="Quartz.Entity"/>;
/// the entity's LocalPosition is the node's graph position.
///
/// Only the scalar auto-properties below are serialized (the Quartz auto-serializer handles
/// string/int/bool/enum/Vector2/... properties). Outgoing connections are a collection, so they
/// ride in the component Data blob via Serialize/DeserializeData. Runtime-only state is kept in
/// plain fields so it never reaches the file.
/// </summary>
public class PowerNodeComponent : Component
{
    // ── Serialized authoring data ──────────────────────────────────────────
    public NodeKind NodeKind { get; set; } = NodeKind.Normal;

    /// <summary>Marks a root/seed node: always a valid initial drop target. Supplies no power on its
    /// own — it becomes a source only once a token is placed on it (like any other node).</summary>
    public bool IsAnchor { get; set; }
    public bool IsGoal { get; set; }

    /// <summary>Power of the token this node hides until powered (0 = holds nothing). Discovery model.</summary>
    public int HeldTokenPower { get; set; }

    /// <summary>
    /// Names of the entities this node has directed connections to. Stored as a field (not a
    /// property) so the auto-serializer ignores it; persisted via Serialize/DeserializeData.
    /// References are by entity Name because entity Guids are regenerated on every load.
    /// </summary>
    public List<string> OutgoingNodeNames = new();

    // ── Runtime-only state (fields → never serialized) ─────────────────────
    public bool Removed;
    public int PlacedTokenPower;
    public bool IsActive;
    public readonly HashSet<PowerNodeComponent> PoweredFrom = new();

    /// <summary>Discovery: whether the player has revealed this node yet (sticky for the session).</summary>
    public bool Discovered;

    /// <summary>Whether this node's held token has already been granted to the inventory.</summary>
    public bool HeldTokenCollected;

    public override string SerializeData()
        => OutgoingNodeNames.Count > 0 ? string.Join(",", OutgoingNodeNames) : string.Empty;

    public override void DeserializeData(string data)
    {
        OutgoingNodeNames.Clear();
        if (string.IsNullOrWhiteSpace(data)) return;

        OutgoingNodeNames.AddRange(
            data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
