using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ld59.UI.Powergrid;

/// <summary>
/// A runtime, directed edge between two nodes. Rebuilt each session from the node components'
/// outgoing references — never serialized. Mirrors the original ConnectionLineSegment.
/// </summary>
public class Connection
{
    public PowerNodeComponent From;
    public PowerNodeComponent To;

    /// <summary>Padded endpoints (offset from node centres) used for crossing/lock geometry.</summary>
    public Vector2 StartPos;
    public Vector2 EndPos;

    /// <summary>True when this edge is currently carrying power (drives line colour/arrow).</summary>
    public bool IsActive;

    /// <summary>Other connections this edge physically crosses; only one of a crossing pair may be powered.</summary>
    public List<Connection> CompetingConnections;

    /// <summary>Locks whose segment crosses this edge; while any is locked, no power flows here.</summary>
    public List<EdgeLockComponent> Locks;
}
