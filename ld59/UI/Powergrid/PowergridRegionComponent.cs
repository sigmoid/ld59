using Microsoft.Xna.Framework;
using Quartz.Components;

namespace ld59.UI.Powergrid;

/// <summary>
/// Marks an axis-aligned region of the puzzle that limits how many runes of a given tier may be
/// placed inside it. The region is a rectangle centred on the entity's world position with half-extents
/// <see cref="HalfWidth"/> × <see cref="HalfHeight"/>. Any node whose position falls inside the
/// rectangle counts toward <see cref="MaxCount"/>.
///
/// Authors add regions via the Region editor tool (drag to draw). In play, the region is drawn as an
/// outlined box with a live counter; it turns red when the limit is exceeded.
/// </summary>
public class PowergridRegionComponent : Component
{
    // ── Serialized ────────────────────────────────────────────────────────────
    public float HalfWidth  { get; set; } = 3f;
    public float HalfHeight { get; set; } = 2f;

    /// <summary>The tier whose rune count is constrained within this region.</summary>
    public int Tier { get; set; } = 1;

    /// <summary>Maximum allowed runes of <see cref="Tier"/> within the region.</summary>
    public int MaxCount { get; set; } = 1;

    // ── Runtime-only (never serialized — fields, not properties) ─────────────
    public bool IsViolated;
    public int  CurrentCount;

    public bool Contains(Vector2 worldPos)
    {
        var c = Entity.Position;
        return System.MathF.Abs(worldPos.X - c.X) <= HalfWidth &&
               System.MathF.Abs(worldPos.Y - c.Y) <= HalfHeight;
    }
}
