using System;
using Microsoft.Xna.Framework;

namespace ld59.WalkingSim;

// First-person walker: a point constrained to a navmesh. The caller feeds a desired
// horizontal direction each frame; the controller edge-walks the mesh and produces an eye
// position. Vertical height follows the mesh (critically-damped smoothing keeps stair-shaped
// ribbons from popping). No gravity, no jumping — none of it exists. Graphics- and
// input-free so it is unit-testable headlessly.
public sealed class WalkController
{
    public NavMesh Mesh { get; }
    public int Triangle { get; private set; } = -1;
    public Vector3 Position { get; private set; }   // point on the mesh (Y = floor height)
    public float EyeHeight { get; set; } = 1.6f;
    public float MoveSpeed { get; set; } = 3f;
    public float HeightSmoothTime { get; set; } = 0.08f;

    private float _smoothY;
    private float _smoothYVel;

    public WalkController(NavMesh mesh)
    {
        Mesh = mesh;
    }

    // Place the walker at a world position. Returns false if that position is off the mesh.
    public bool Spawn(Vector3 pos)
    {
        int tri = Mesh.FindTriangle(pos);
        if (tri < 0) return false;
        Triangle = tri;
        float y = Mesh.HeightAt(tri, pos.X, pos.Z);
        Position = new Vector3(pos.X, y, pos.Z);
        _smoothY = y;
        _smoothYVel = 0f;
        return true;
    }

    // Move by a horizontal direction (need not be normalized) for dt seconds.
    public void Move(Vector2 dirXZ, float dt)
    {
        if (Triangle < 0) return;

        float len = dirXZ.Length();
        if (len > 1e-5f)
        {
            Vector2 delta = dirXZ / len * (MoveSpeed * dt);
            var (pos, tri) = Mesh.Move(Triangle, Position, delta);
            Position = pos;
            Triangle = tri;
        }

        _smoothY = SmoothDamp(_smoothY, Position.Y, ref _smoothYVel, HeightSmoothTime, dt);
    }

    // Camera eye position: smoothed floor height plus eye offset.
    public Vector3 EyePosition => new Vector3(Position.X, _smoothY + EyeHeight, Position.Z);

    // Critically-damped spring toward a target (same formulation as Unity's Mathf.SmoothDamp).
    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        smoothTime = MathF.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        float output = target + (change + temp) * exp;
        return output;
    }
}
