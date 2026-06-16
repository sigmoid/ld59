using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

public class PinballEngine
{
    private class PinballRaycastResult
    {
        public PinballObstacle Obstacle;
        public Vector2 Point;
        public Vector2 Normal;
        public float Distance;
    }

    private class RampTraversal
    {
        public PinballRamp Ramp;
        public int   Segment; // ball is between Path[Segment] and Path[Segment+1]
        public float T;       // [0,1] progress within the current segment
        public float Speed;   // signed game-units/second along the path direction
    }

    private PinballTable _table;
    private Texture2D    _pixel;
    private readonly Dictionary<PinballBall, RampTraversal> _rampBalls = new();

    public PinballEngine(PinballTable table)
    {
        _table = table;
    }

    public void PlaceBalls(Vector2 tablePos)
    {
        foreach (var ball in _table.Balls)
        {
            ball.Center   = tablePos;
            ball.Velocity = Vector2.Zero;
        }
    }

    // Physics runs at a fixed rate so the simulation is deterministic and
    // independent of frame rate. The flipper is the fastest-moving body, so a
    // fine step keeps its rotation (and therefore the ball's launch angle) from
    // being quantized to a coarse, frame-rate-dependent slice of the swing.
    public float PhysicsHz     { get; set; } = 240f;
    public float MaxBallSpeed  { get; set; } = 2000f;

    // Fraction of relative tangential velocity removed per flipper contact (0 = no friction, 1 = full grip)
    public float FlipperFriction { get; set; } = 0.25f;

    private float _accumulator;

    public void Update(float deltaTime)
    {
        float stepDt = 1f / PhysicsHz;

        // Invariant for tunnel-free penetration handling: a ball must not be able
        // to cross the (thin) flipper segment in a single step. Holds while
        // MaxBallSpeed * stepDt < ballRadius.
        _accumulator += deltaTime;
        // Clamp after a hitch so we don't spiral trying to catch up.
        if (_accumulator > 0.25f) _accumulator = 0.25f;

        while (_accumulator >= stepDt)
        {
            Step(stepDt);
            _accumulator -= stepDt;
        }
    }

    private void Step(float stepDt)
    {
        foreach (var ball in _table.Balls)
        {
            if (_rampBalls.TryGetValue(ball, out var trav))
                UpdateRampBall(ball, trav, stepDt);
            else
                UpdateBall(ball, stepDt);
        }

        var flipperSnapshots = new List<(PinballFlipper Flipper, float PrevAngle)>();
        foreach (var obstacle in _table.Obstacles)
            if (obstacle is PinballFlipper f)
                flipperSnapshots.Add((f, f.CurrentAngle));

        _table.Update(stepDt);

        foreach (var ball in _table.Balls)
        {
            if (_rampBalls.ContainsKey(ball)) continue;
            DepenetrateBall(ball);
            // Flipper contact is resolved in exactly one place (the swept resolver),
            // using the flipper's post-rotation state, so a single substep can't
            // apply two inconsistent impulses from two different code paths.
            foreach (var (flipper, prevAngle) in flipperSnapshots)
                ResolveFlipperSweep(ball, flipper, prevAngle);
        }

        foreach (var ball in _table.Balls)
            if (!_rampBalls.ContainsKey(ball))
                TryEnterRamp(ball);
    }

    public void DebugDraw(SpriteBatch batch, Vector2 offset = default)
    {
        foreach(var obstacle in _table.Obstacles)
        {
            if(obstacle is PinballWall wall)
            {
                int segCount = wall.IsOpen ? wall.Vertices.Count - 1 : wall.Vertices.Count;
                for(int i = 0; i < segCount; i++)
                {
                    var start = wall.Vertices[i] + offset;
                    var end   = wall.Vertices[(i + 1) % wall.Vertices.Count] + offset;
                    DrawLine(start, end, batch);
                }
            }

            if(obstacle is PinballCircleCollider circle)
            {
                DrawCircle(circle.Center + offset, circle.Radius, batch);
            }

            if(obstacle is PinballFlipper paddle)
            {
                var paddleStart = paddle.HingePosition;
                var paddleEnd = paddle.HingePosition + new Vector2(
                    MathF.Cos(paddle.CurrentAngle),
                    MathF.Sin(paddle.CurrentAngle)
                ) * paddle.Length;
                DrawLine(paddleStart + offset, paddleEnd + offset, batch);
            }

            if(obstacle is PinballRamp ramp)
            {
                for(int i = 0; i < ramp.Path.Count - 1; i++)
                {
                    var s = new Vector2(ramp.Path[i].X,     ramp.Path[i].Y)     + offset;
                    var e = new Vector2(ramp.Path[i + 1].X, ramp.Path[i + 1].Y) + offset;
                    DrawLine(s, e, batch, 3f, new Color(255, 200, 0));
                }
                DrawCircle(new Vector2(ramp.Path[0].X, ramp.Path[0].Y) + offset,
                           ramp.EntryRadius, batch, new Color(0, 200, 50));
            }
        }

        foreach(var ball in _table.Balls)
        {
            DrawCircle(ball.Center + offset, ball.Radius, batch, Color.White, 2);
        }
    }

    private void UpdateBall(PinballBall ball, float deltaTime)
    {
        if (float.IsNaN(ball.Velocity.X) || float.IsNaN(ball.Velocity.Y) ||
            float.IsNaN(ball.Center.X)   || float.IsNaN(ball.Center.Y))
        {
            ball.Velocity = Vector2.Zero;
            return;
        }

        ball.Velocity += new Vector2(0, _table.DefaultAcceleration * deltaTime);
        ball.Velocity *= MathF.Exp(-_table.Damping * deltaTime);

        Vector2 delta = ball.Velocity * deltaTime;
        float moveLen = delta.Length();
        if (moveLen < 1e-6f) return;

        var ray = Raycast(ball, delta / moveLen, moveLen);

        const float restitution = 0.65f;

        if (ray != null)
        {
            ball.Center = ray.Point;

            // Only static obstacles (walls) reach here — flipper contact is handled
            // exclusively by ResolveFlipperSweep after the flipper has rotated.
            float vrelN = Vector2.Dot(ball.Velocity, ray.Normal);  // < 0 guaranteed by raycast

            float e  = -vrelN < _table.RestingVelocityThreshold ? 0f : restitution;
            float jn = -(1f + e) * vrelN;
            ball.Velocity += jn * ray.Normal;

            // Continue with the remaining travel distance after the bounce
            float remaining = moveLen - ray.Distance;
            float speed = ball.Velocity.Length();
            if (remaining > 1e-4f && speed > 1e-6f)
                ball.Center += (ball.Velocity / speed) * remaining;
        }
        else
        {
            ball.Center += delta;
        }

        float spd = ball.Velocity.Length();
        if (spd > MaxBallSpeed)
            ball.Velocity *= MaxBallSpeed / spd;
    }

    private void DepenetrateBall(PinballBall ball)
    {
        const float restitution = 0.65f;

        foreach (var obstacle in _table.Obstacles)
        {
            if (obstacle == ball) continue;
            if (obstacle is not PinballWall w) continue;

            foreach (var (a, b) in WallSegments(w))
            {
                if (!SegmentCircleOverlap(ball.Center, ball.Radius, a, b, out float depth, out var normal))
                    continue;

                ball.Center += normal * depth;

                float vDotN = Vector2.Dot(ball.Velocity, normal);
                if (vDotN < 0f)
                {
                    float coeff = -vDotN < _table.RestingVelocityThreshold ? 0f : restitution;
                    ball.Velocity -= (1f + coeff) * vDotN * normal;
                }
            }
        }
    }

    // Resolves flipper-swept collisions using the pre-rotation side of the ball
    // to guarantee the correct outward normal even when the flipper sweeps past quickly.
    private void ResolveFlipperSweep(PinballBall ball, PinballFlipper flipper, float prevAngle)
    {
        const float restitution = 0.65f;

        // Determine which side of the flipper the ball was on BEFORE this frame's rotation.
        // We use the old segment normal so the push direction is always consistent.
        var prevDir  = new Vector2(MathF.Cos(prevAngle), MathF.Sin(prevAngle));
        var prevSegN = new Vector2(-prevDir.Y, prevDir.X);
        float prevSide = Vector2.Dot(ball.Center - flipper.HingePosition, prevSegN) >= 0f ? 1f : -1f;

        // Test penetration against the flipper at its NEW position
        var a = flipper.HingePosition;
        var b = a + new Vector2(MathF.Cos(flipper.CurrentAngle), MathF.Sin(flipper.CurrentAngle)) * flipper.Length;
        if (!SegmentCircleOverlap(ball.Center, ball.Radius, a, b, out float depth, out _))
            return;

        // Force the normal to match the pre-frame side regardless of geometry
        var currDir  = new Vector2(MathF.Cos(flipper.CurrentAngle), MathF.Sin(flipper.CurrentAngle));
        var normal   = prevSide * new Vector2(-currDir.Y, currDir.X);

        ball.Center += normal * depth;

        // Use velocity relative to the flipper surface so the impulse fires exactly once:
        // when relVDotN >= 0 the ball is already separating in the flipper's frame, so skip.
        var arm        = ball.Center - flipper.HingePosition;
        var surfaceVel = flipper.AngularVelocity * new Vector2(-arm.Y, arm.X);
        float relVDotN = Vector2.Dot(ball.Velocity - surfaceVel, normal);

        if (relVDotN < 0f)
        {
            var vrel = ball.Velocity - surfaceVel; // pre-impulse relative velocity

            // A swinging flipper always launches the ball with full restitution so a
            // catch-and-shoot is consistent. The resting-velocity threshold (which
            // suppresses bounce to keep a ball stable on a still flipper) only applies
            // when the flipper isn't actively moving.
            bool  swinging = MathF.Abs(flipper.AngularVelocity) > 0.5f;
            float e  = (!swinging && -relVDotN < _table.RestingVelocityThreshold) ? 0f : restitution;
            float jn = -(1f + e) * relVDotN;
            ball.Velocity += jn * normal;

            if (swinging)
            {
                var   tangent = new Vector2(-normal.Y, normal.X);
                float vrelT   = Vector2.Dot(vrel, tangent);
                float jt      = Math.Clamp(-vrelT, -FlipperFriction * jn, FlipperFriction * jn);
                ball.Velocity += jt * tangent;
            }
        }
    }

    private static bool SegmentCircleOverlap(Vector2 center, float radius,
                                             Vector2 a, Vector2 b,
                                             out float depth, out Vector2 normal)
    {
        depth  = 0f;
        normal = Vector2.Zero;

        var ab  = b - a;
        float len = ab.Length();
        if (len < 1e-6f) return false;

        var abDir   = ab / len;
        float t     = Math.Clamp(Vector2.Dot(center - a, abDir), 0f, len);
        var closest = a + abDir * t;

        var diff = center - closest;
        float dist = diff.Length();
        if (dist >= radius) return false;

        depth  = radius - dist;
        normal = dist > 1e-6f ? diff / dist : new Vector2(-abDir.Y, abDir.X);
        return true;
    }

    private void DrawCircle(Vector2 position, float radius, SpriteBatch batch, Color color = default, float thickness = 2.0f, int segments = 32)
    {
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = step * i;
            float a1 = step * (i + 1);
            var p0 = position + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = position + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            DrawLine(p0, p1, batch, thickness, color);
        }
    }

    private void DrawLine(Vector2 start, Vector2 end, SpriteBatch batch, float thickness = 2.0f, Color color = default)
    {
        if (_pixel == null)
        {
            _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Microsoft.Xna.Framework.Color.White });
        }

        var diff = end - start;
        float length = diff.Length();
        float angle = MathF.Atan2(diff.Y, diff.X);

        var xnaColor = color == default
            ? Microsoft.Xna.Framework.Color.White
            : new Microsoft.Xna.Framework.Color(color.R, color.G, color.B, color.A);

        batch.Draw(
            _pixel,
            new Microsoft.Xna.Framework.Vector2(start.X, start.Y),
            null,
            xnaColor,
            angle,
            new Microsoft.Xna.Framework.Vector2(0, 0.5f),
            new Microsoft.Xna.Framework.Vector2(length, thickness),
            SpriteEffects.None,
            0f
        );
    }

    private PinballRaycastResult Raycast(PinballBall ball, Vector2 direction, float distance)
    {
        var origin = ball.Center;
        var dir    = Vector2.Normalize(direction);
        float r    = ball.Radius;

        PinballRaycastResult best = null;

        foreach (var obstacle in _table.Obstacles)
        {
            if (obstacle == ball) continue;

            // Flippers are intentionally excluded: their contact is resolved by the
            // penetration-based swept resolver, not by the ball's own raycast.
            IEnumerable<(Vector2 A, Vector2 B)> segments = obstacle switch
            {
                PinballWall w => WallSegments(w),
                _             => null,
            };
            if (segments == null) continue;

            foreach (var (a, b) in segments)
            {
                if (!RaycastSegment(origin, dir, distance, r, a, b, out float t, out var normal))
                    continue;
                if (best == null || t < best.Distance)
                    best = new PinballRaycastResult { Obstacle = obstacle, Point = origin + dir * t, Normal = normal, Distance = t };
            }
        }

        return best;
    }

    private static IEnumerable<(Vector2, Vector2)> WallSegments(PinballWall wall)
    {
        int segCount = wall.IsOpen ? wall.Vertices.Count - 1 : wall.Vertices.Count;
        for (int i = 0; i < segCount; i++)
            yield return (wall.Vertices[i], wall.Vertices[(i + 1) % wall.Vertices.Count]);
    }

    // Swept-circle cast against a line segment.
    // Returns the earliest t in [0, maxDist] where a circle of `radius` centred on
    // origin + t*dir first touches the segment (shaft or either endpoint cap).
    private static bool RaycastSegment(Vector2 origin, Vector2 dir, float maxDist, float radius,
                                       Vector2 a, Vector2 b, out float t, out Vector2 normal)
    {
        t      = float.MaxValue;
        normal = Vector2.Zero;

        var ab    = b - a;
        float len = ab.Length();
        if (len < 1e-6f) return false;

        var abDir = ab / len;
        // Segment normal pointing "left" of a->b
        var segN = new Vector2(-abDir.Y, abDir.X);

        float d0 = Vector2.Dot(origin - a, segN); // signed dist: origin to line
        float dd = Vector2.Dot(dir, segN);          // rate of change along ray

        // Shaft: find when the circle surface (on origin's side) touches the infinite line
        if (MathF.Abs(dd) > 1e-6f)
        {
            float tShaft = (MathF.Sign(d0) * radius - d0) / dd;
            if (tShaft >= 0f && tShaft <= maxDist)
            {
                float s = Vector2.Dot(origin + tShaft * dir - a, abDir);
                if (s >= 0f && s <= len)
                {
                    t      = tShaft;
                    normal = MathF.Sign(d0) * segN;
                }
            }
        }

        // Caps: hemisphere at each endpoint
        TestCap(origin, dir, maxDist, radius, a, ref t, ref normal);
        TestCap(origin, dir, maxDist, radius, b, ref t, ref normal);

        return t < float.MaxValue;
    }

    private static void TestCap(Vector2 origin, Vector2 dir, float maxDist, float radius,
                                Vector2 cap, ref float bestT, ref Vector2 bestN)
    {
        var oc    = origin - cap;
        float b   = Vector2.Dot(oc, dir);
        float c   = oc.LengthSquared() - radius * radius;
        float disc = b * b - c;
        if (disc < 0f) return;

        float tCap = -b - MathF.Sqrt(disc);
        if (tCap >= 0f && tCap <= maxDist && tCap < bestT)
        {
            var capNormal = origin + tCap * dir - cap;
            float capLen  = capNormal.Length();
            if (capLen < 1e-6f) return;
            bestT = tCap;
            bestN = capNormal / capLen;
        }
    }

    // ── Ramp traversal ────────────────────────────────────────────────────────

    private void UpdateRampBall(PinballBall ball, RampTraversal trav, float dt)
    {
        var path = trav.Ramp.Path;

        // Gravity component along the current segment (inclined-plane formula)
        var   a        = path[trav.Segment];
        var   b        = path[trav.Segment + 1];
        float seg2dLen = new Vector2(b.X - a.X, b.Y - a.Y).Length();
        float dZ       = b.Z - a.Z;
        float seg3dLen = MathF.Sqrt(seg2dLen * seg2dLen + dZ * dZ);
        if (seg3dLen > 1e-6f)
            trav.Speed -= _table.DefaultAcceleration * (dZ / seg3dLen) * dt;

        trav.Speed *= MathF.Exp(-_table.Damping * dt);

        // Walk along the path consuming the distance for this timestep
        float dist = trav.Speed * dt;
        while (MathF.Abs(dist) > 1e-6f && trav.Segment >= 0 && trav.Segment < path.Count - 1)
        {
            a        = path[trav.Segment];
            b        = path[trav.Segment + 1];
            seg2dLen = new Vector2(b.X - a.X, b.Y - a.Y).Length();
            if (seg2dLen < 1e-6f) break;

            if (dist > 0f)
            {
                float room = (1f - trav.T) * seg2dLen;
                if (dist <= room) { trav.T += dist / seg2dLen; dist = 0f; }
                else              { dist -= room; trav.Segment++; trav.T = 0f; }
            }
            else
            {
                float room = trav.T * seg2dLen;
                if (-dist <= room) { trav.T += dist / seg2dLen; dist = 0f; }
                else               { dist += room; trav.Segment--; trav.T = 1f; }
            }
        }

        if (trav.Segment >= path.Count - 1) { ExitRamp(ball, trav, forward: true);  return; }
        if (trav.Segment < 0)               { ExitRamp(ball, trav, forward: false); return; }

        a = path[trav.Segment];
        b = path[trav.Segment + 1];
        ball.Center = Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), trav.T);
    }

    private void TryEnterRamp(PinballBall ball)
    {
        const float minEntrySpeed = 50f;

        foreach (var obstacle in _table.Obstacles)
        {
            if (obstacle is not PinballRamp ramp || ramp.Path.Count < 2) continue;

            var entry = new Vector2(ramp.Path[0].X, ramp.Path[0].Y);
            if (Vector2.Distance(ball.Center, entry) > ramp.EntryRadius) continue;

            var entryDir = Vector2.Normalize(
                new Vector2(ramp.Path[1].X - ramp.Path[0].X, ramp.Path[1].Y - ramp.Path[0].Y));
            float speed = Vector2.Dot(ball.Velocity, entryDir);
            if (speed < minEntrySpeed) continue;

            ball.Center   = entry;
            ball.Velocity = Vector2.Zero;
            _rampBalls[ball] = new RampTraversal { Ramp = ramp, Segment = 0, T = 0f, Speed = speed };
            break;
        }
    }

    private void ExitRamp(PinballBall ball, RampTraversal trav, bool forward)
    {
        var path = trav.Ramp.Path;
        Vector2 exitPos, exitDir;

        if (forward)
        {
            exitPos = new Vector2(path[^1].X, path[^1].Y);
            var d   = exitPos - new Vector2(path[^2].X, path[^2].Y);
            exitDir = d.LengthSquared() > 1e-12f ? Vector2.Normalize(d) : Vector2.UnitX;
        }
        else
        {
            exitPos = new Vector2(path[0].X, path[0].Y);
            var d   = exitPos - new Vector2(path[1].X, path[1].Y);
            exitDir = d.LengthSquared() > 1e-12f ? Vector2.Normalize(d) : -Vector2.UnitX;
        }

        ball.Center   = exitPos;
        ball.Velocity = exitDir * MathF.Abs(trav.Speed);
        _rampBalls.Remove(ball);
    }
}