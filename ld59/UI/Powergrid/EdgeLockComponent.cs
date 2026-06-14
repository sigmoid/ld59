using Microsoft.Xna.Framework;
using Quartz.Components;

namespace ld59.UI.Powergrid;

/// <summary>
/// A lock placed across the board as a segment (PointA..PointB). Any connection whose line
/// crosses this segment cannot carry power while the lock is locked. The lock is unlocked while
/// its key node (referenced by entity name) is powered.
///
/// PointA/PointB are world-space graph coordinates. KeyNode is an entity name resolved by the
/// controller after load (entity Guids are regenerated each load, so references go by name).
/// </summary>
public class EdgeLockComponent : Component
{
    public Vector2 PointA { get; set; }
    public Vector2 PointB { get; set; }
    public string KeyNode { get; set; } = string.Empty;

    // ── Runtime-only state (fields → never serialized) ─────────────────────
    public bool IsLocked = true;
    public PowerNodeComponent KeyNodeRef;
}
