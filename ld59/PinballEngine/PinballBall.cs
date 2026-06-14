using System;
using Microsoft.Xna.Framework;

public class PinballBall : PinballCircleCollider
{
    public Vector2 Velocity {get;set;}

    public PinballBall(float radius, Vector2 position) 
    {
        Radius = radius;
        Center = position;
    }
}