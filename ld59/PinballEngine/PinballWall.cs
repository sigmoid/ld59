using System.Collections.Generic;
using Microsoft.Xna.Framework;
public class PinballWall : PinballObstacle
{
    public List<Vector2> Vertices { get; set; }
    public bool IsOpen { get; set; } = false;
}