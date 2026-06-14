using System.Collections.Generic;
using Microsoft.Xna.Framework;

public class PinballRamp : PinballObstacle
{
    // 3D path: X/Y = table-space position, Z = height in game units above the table surface.
    // Entry = Path[0], exit = Path[^1].
    public List<Vector3> Path { get; set; }

    // Ball must be within this radius of Path[0] and moving roughly toward Path[1] to be captured.
    public float EntryRadius { get; set; }
}
