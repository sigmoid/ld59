using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ld59.WalkingSim;

// A navmesh the player walks on as a point. Movement is an edge-walk across triangle
// adjacency: crossing a shared edge enters the neighbour, hitting a boundary edge slides
// along it. Floor height comes from the current triangle's plane. There is no collision
// resolution and no gravity — if it is in the mesh, it is walkable, by construction.
//
// All math is winding-agnostic (works with either triangle winding) so meshes exported
// from Blender need no special handling. Uses only Vector2/Vector3 so it is unit-testable
// in the headless WalkingSimTests harness.
public sealed class NavMesh
{
    // Per-edge neighbour triangle index, -1 for a boundary (wall) edge.
    // Edge 0 = V0->V1, edge 1 = V1->V2, edge 2 = V2->V0.
    public struct NavTriangle
    {
        public int V0, V1, V2;
        public int N01, N12, N20;

        public int Vert(int i) => i == 0 ? V0 : i == 1 ? V1 : V2;
        public int Neighbour(int edge) => edge == 0 ? N01 : edge == 1 ? N12 : N20;
    }

    public readonly Vector3[] Vertices;
    public readonly NavTriangle[] Triangles;

    private const float Eps = 1e-4f;          // general planar tolerance
    private const float ForwardEps = 1e-5f;   // minimum forward travel before an edge crossing counts
    private const float NudgeEps = 1e-4f;      // push across / inside an edge to avoid re-detecting it
    private const int MaxMoveIterations = 16;
    private const float CornerProbe = 3e-3f;   // inward bias when probing out of a wall-vertex wedge
    private const float TJunctionProbe = 1e-2f; // step across a boundary edge to detect a T-junction neighbour
    private static readonly float[] LimboProbeReaches = { 3e-3f, 1.5e-2f, 5e-2f }; // forward reaches tried to find a continuation
    private const float LimboPeel = 1e-2f;     // inward step when wedged, to clear a degenerate vertex fast
    private const float MaxStepHeight = 0.5f;   // reject a probe continuation whose floor is this far (m) above/below --
                                                // keeps a wall probe from "finding" a roof/floor that only overlaps in XZ

    private NavMesh(Vector3[] vertices, NavTriangle[] triangles)
    {
        Vertices = vertices;
        Triangles = triangles;
    }

    // Build a navmesh from raw vertices and triangle indices. Coincident vertices are welded
    // (so adjacency can be detected from shared indices), degenerate triangles are dropped.
    public static NavMesh Build(IReadOnlyList<Vector3> verts, IReadOnlyList<(int a, int b, int c)> faces, float weldEps = 1e-3f)
    {
        // --- weld coincident vertices onto a quantized grid ---
        var canonical = new List<Vector3>();
        var remap = new int[verts.Count];
        var grid = new Dictionary<(long, long, long), int>();
        float inv = 1f / weldEps;
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            var key = ((long)MathF.Round(v.X * inv), (long)MathF.Round(v.Y * inv), (long)MathF.Round(v.Z * inv));
            if (!grid.TryGetValue(key, out int idx))
            {
                idx = canonical.Count;
                canonical.Add(v);
                grid[key] = idx;
            }
            remap[i] = idx;
        }

        // --- remap faces, drop degenerates ---
        var tris = new List<NavTriangle>(faces.Count);
        foreach (var (a, b, c) in faces)
        {
            int ra = remap[a], rb = remap[b], rc = remap[c];
            if (ra == rb || rb == rc || rc == ra) continue;
            tris.Add(new NavTriangle { V0 = ra, V1 = rb, V2 = rc, N01 = -1, N12 = -1, N20 = -1 });
        }

        var triArr = tris.ToArray();

        // --- adjacency: match undirected vertex pairs shared by two triangles ---
        var edgeOwner = new Dictionary<(int lo, int hi), (int tri, int edge)>();
        for (int t = 0; t < triArr.Length; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                int va = triArr[t].Vert(e);
                int vb = triArr[t].Vert((e + 1) % 3);
                var key = va < vb ? (va, vb) : (vb, va);
                if (edgeOwner.TryGetValue(key, out var other))
                {
                    LinkEdge(triArr, t, e, other.tri);
                    LinkEdge(triArr, other.tri, other.edge, t);
                }
                else
                {
                    edgeOwner[key] = (t, e);
                }
            }
        }

        return new NavMesh(canonical.ToArray(), triArr);
    }

    private static void LinkEdge(NavTriangle[] tris, int t, int edge, int neighbour)
    {
        switch (edge)
        {
            case 0: tris[t].N01 = neighbour; break;
            case 1: tris[t].N12 = neighbour; break;
            default: tris[t].N20 = neighbour; break;
        }
    }

    // Locate the triangle under a world position. Where several triangles overlap in XZ
    // (e.g. a tunnel passing under a platform) the one whose plane height is nearest pos.Y
    // is chosen, which is what keeps overlapping surfaces unambiguous. Returns -1 if off-mesh.
    public int FindTriangle(Vector3 pos)
    {
        int best = -1;
        float bestDy = float.MaxValue;
        for (int t = 0; t < Triangles.Length; t++)
        {
            if (!PointInTriangleXZ(t, pos.X, pos.Z)) continue;
            float y = HeightAt(t, pos.X, pos.Z);
            float dy = MathF.Abs(y - pos.Y);
            if (dy < bestDy)
            {
                bestDy = dy;
                best = t;
            }
        }
        return best;
    }

    // Best-effort spawn point near `pos`: if pos.XZ is on the mesh, its exact projection (X, floor
    // height, Z); otherwise the centroid of whichever triangle's centroid is closest in XZ. Used
    // for "start game from camera" (editor) and similar approximate placements where a precise
    // walk to the nearest edge isn't needed.
    public Vector3 NearestPointApprox(Vector3 pos)
    {
        int tri = FindTriangle(pos);
        if (tri >= 0)
            return new Vector3(pos.X, HeightAt(tri, pos.X, pos.Z), pos.Z);

        int best = 0;
        float bestDistSq = float.MaxValue;
        for (int t = 0; t < Triangles.Length; t++)
        {
            CentroidXZ(t, out float cx, out float cz);
            float dx = cx - pos.X, dz = cz - pos.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestDistSq) { bestDistSq = d2; best = t; }
        }
        CentroidXZ(best, out float bx, out float bz);
        return new Vector3(bx, HeightAt(best, bx, bz), bz);
    }

    // Height of the current triangle's plane at (x, z).
    public float HeightAt(int tri, float x, float z)
    {
        var a = Vertices[Triangles[tri].V0];
        var b = Vertices[Triangles[tri].V1];
        var c = Vertices[Triangles[tri].V2];

        // plane normal = (b-a) x (c-a)
        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        float nx = uy * vz - uz * vy;
        float ny = uz * vx - ux * vz;
        float nz = ux * vy - uy * vx;

        if (MathF.Abs(ny) < 1e-6f)
            return (a.Y + b.Y + c.Y) / 3f; // near-vertical / degenerate guard

        // n . (P - a) = 0  ->  y = a.Y - (nx*(x-a.X) + nz*(z-a.Z)) / ny
        return a.Y - (nx * (x - a.X) + nz * (z - a.Z)) / ny;
    }

    // Walk from a position on triangle `tri` by a horizontal delta, sliding along boundary
    // edges and crossing into neighbours over shared edges. Returns the new position (with Y
    // snapped to the resulting triangle) and the resulting triangle index.
    public (Vector3 pos, int tri) Move(int tri, Vector3 pos, Vector2 deltaXZ)
    {
        if (tri < 0) return (pos, tri);

        float curX = pos.X, curZ = pos.Z;
        float remX = deltaXZ.X, remZ = deltaXZ.Y;
        int cur = tri;

        for (int iter = 0; iter < MaxMoveIterations; iter++)
        {
            if (remX * remX + remZ * remZ < ForwardEps * ForwardEps) break;

            float tgtX = curX + remX, tgtZ = curZ + remZ;
            if (PointInTriangleXZ(cur, tgtX, tgtZ))
            {
                curX = tgtX; curZ = tgtZ;
                break;
            }

            // find the edge exited first (smallest forward parameter along cur->target)
            int hitEdge = -1;
            float hitT = float.MaxValue;
            for (int e = 0; e < 3; e++)
            {
                var pa = Vertices[Triangles[cur].Vert(e)];
                var pb = Vertices[Triangles[cur].Vert((e + 1) % 3)];
                if (SegmentEdgeCross(curX, curZ, remX, remZ, pa.X, pa.Z, pb.X, pb.Z, out float t) && t < hitT)
                {
                    hitT = t;
                    hitEdge = e;
                }
            }

            if (hitEdge < 0)
            {
                // The straight target lies outside cur, yet no edge registered a crossing. This is
                // the wall-riding degeneracy: the remaining motion runs parallel to a boundary edge
                // the walker sits on (a parallel edge never counts as "hit"), or it points out
                // through a shared vertex. This is what used to pin the walker mid-slide ("stuck on
                // a corner"); the probe + peel below break it.
                CentroidXZ(cur, out float cx, out float cz);
                float inx = cx - curX, inz = cz - curZ;
                float inl = MathF.Sqrt(inx * inx + inz * inz);
                if (inl < 1e-6f) break;
                inx /= inl; inz /= inl;

                // First try to round the corner / continue along the wall: probe along the motion --
                // pulled slightly inward so a walker riding exactly on the boundary line still lands
                // on the floor -- and see if the mesh continues in a NEW triangle. Try increasing
                // reaches so a continuation a little further along (past a small notch or convex
                // vertex) is still found rather than crawling through it. FindTriangle guarantees the
                // landing is on the mesh, and requiring a different triangle keeps genuine apices
                // (where every reach lands off-mesh) stopping cleanly.
                float rl = MathF.Sqrt(remX * remX + remZ * remZ);
                bool hopped = false;
                if (rl > 1e-6f)
                {
                    float ux = remX / rl, uz = remZ / rl;
                    float curY = HeightAt(cur, curX, curZ);
                    foreach (float reach in LimboProbeReaches)
                    {
                        float step = MathF.Min(rl, reach);
                        float probeX = curX + ux * step + inx * CornerProbe;
                        float probeZ = curZ + uz * step + inz * CornerProbe;
                        int landed = FindTriangle(new Vector3(probeX, curY, probeZ));
                        if (landed >= 0 && landed != cur &&
                            MathF.Abs(HeightAt(landed, probeX, probeZ) - curY) <= MaxStepHeight)
                        {
                            curX = probeX; curZ = probeZ;
                            remX -= ux * step; remZ -= uz * step;
                            cur = landed;
                            hopped = true;
                            break;
                        }
                    }
                }
                if (hopped) continue;

                // No continuation ahead: the walker is wedged against a boundary with the motion
                // parallel to it. Peel it toward the interior by a real amount (bounded by the
                // remaining motion) so it clears the degenerate vertex within a frame or two instead
                // of the multi-second crawl a tiny nudge produced. Moving toward the centroid keeps
                // it inside the mesh, and the final clamp guards any residual drift.
                float peel = MathF.Min(rl, LimboPeel);
                curX += inx * peel;
                curZ += inz * peel;
                continue;
            }

            float hx = curX + remX * hitT;
            float hz = curZ + remZ * hitT;
            int neighbour = Triangles[cur].Neighbour(hitEdge);

            if (neighbour >= 0)
            {
                // cross into the neighbour, consuming the travelled portion
                curX = hx; curZ = hz;
                remX *= (1f - hitT);
                remZ *= (1f - hitT);
                // nudge across the shared edge so we do not immediately re-detect it
                float rl = MathF.Sqrt(remX * remX + remZ * remZ);
                if (rl > 1e-6f)
                {
                    curX += remX / rl * NudgeEps;
                    curZ += remZ / rl * NudgeEps;
                }
                cur = neighbour;
            }
            else
            {
                // boundary edge: slide the leftover motion along the wall
                var pa = Vertices[Triangles[cur].Vert(hitEdge)];
                var pb = Vertices[Triangles[cur].Vert((hitEdge + 1) % 3)];
                float ex = pb.X - pa.X, ez = pb.Z - pa.Z;
                float el = MathF.Sqrt(ex * ex + ez * ez);
                if (el < 1e-6f) break;
                ex /= el; ez /= el;

                CentroidXZ(cur, out float cx, out float cz);

                // A Recast-baked mesh can have T-junctions: an adjacent walkable triangle meets this
                // edge without a matching (welded) edge, so it has no neighbour link and looks like a
                // wall. Before treating it as one, probe just across the edge (along its outward
                // normal, so the test is independent of how grazing the approach is); if walkable
                // floor is there, cross into it -- the walker would otherwise pin at the junction. A
                // genuine wall borders non-walkable space, so the probe finds nothing.
                float nx = -ez, nz = ex;                                  // edge normal
                if (nx * (hx - cx) + nz * (hz - cz) < 0f) { nx = -nx; nz = -nz; }  // orient outward
                float acrossX = hx + nx * TJunctionProbe;
                float acrossZ = hz + nz * TJunctionProbe;
                float edgeY = HeightAt(cur, hx, hz);
                int across = FindTriangle(new Vector3(acrossX, edgeY, acrossZ));
                if (across >= 0 && across != cur &&
                    MathF.Abs(HeightAt(across, acrossX, acrossZ) - edgeY) <= MaxStepHeight)
                {
                    curX = acrossX; curZ = acrossZ;
                    remX *= (1f - hitT); remZ *= (1f - hitT);
                    cur = across;
                    continue;
                }

                float leftX = remX * (1f - hitT), leftZ = remZ * (1f - hitT);
                float proj = leftX * ex + leftZ * ez;

                // sit at the contact point pulled slightly back inside the triangle
                float ix = cx - hx, iz = cz - hz;
                float il = MathF.Sqrt(ix * ix + iz * iz);
                curX = hx + (il > 1e-6f ? ix / il * NudgeEps : 0f);
                curZ = hz + (il > 1e-6f ? iz / il * NudgeEps : 0f);

                remX = ex * proj;
                remZ = ez * proj;
            }
        }

        // Safety net: the degenerate-corner handling can leave the position a hair (~1e-4) outside
        // cur through floating-point drift. Snap it back inside so callers never see an off-mesh
        // point (FindTriangle would reject it and the walker would freeze).
        for (int k = 0; k < 4 && !PointInTriangleXZ(cur, curX, curZ); k++)
        {
            CentroidXZ(cur, out float fx, out float fz);
            float dx = fx - curX, dz = fz - curZ;
            float dl = MathF.Sqrt(dx * dx + dz * dz);
            if (dl < 1e-6f) break;
            curX += dx / dl * (NudgeEps * 4f);
            curZ += dz / dl * (NudgeEps * 4f);
        }

        float y = HeightAt(cur, curX, curZ);
        return (new Vector3(curX, y, curZ), cur);
    }

    private void CentroidXZ(int tri, out float x, out float z)
    {
        var a = Vertices[Triangles[tri].V0];
        var b = Vertices[Triangles[tri].V1];
        var c = Vertices[Triangles[tri].V2];
        x = (a.X + b.X + c.X) / 3f;
        z = (a.Z + b.Z + c.Z) / 3f;
    }

    // Intersect the ray (ox,oz)+t*(dx,dz), t in (ForwardEps, 1], with edge segment [a,b] in XZ.
    private static bool SegmentEdgeCross(float ox, float oz, float dx, float dz,
                                         float ax, float az, float bx, float bz, out float t)
    {
        t = 0f;
        float sx = bx - ax, sz = bz - az;
        float denom = dx * sz - dz * sx; // cross(d, s)
        if (MathF.Abs(denom) < 1e-9f) return false; // parallel

        float wx = ax - ox, wz = az - oz;
        float tt = (wx * sz - wz * sx) / denom; // param along the ray
        float uu = (wx * dz - wz * dx) / denom; // param along the edge
        if (tt <= ForwardEps || tt > 1f + Eps) return false;
        if (uu < -Eps || uu > 1f + Eps) return false;
        t = tt;
        return true;
    }

    // Winding-agnostic point-in-triangle test in the XZ plane.
    private bool PointInTriangleXZ(int tri, float px, float pz)
    {
        var a = Vertices[Triangles[tri].V0];
        var b = Vertices[Triangles[tri].V1];
        var c = Vertices[Triangles[tri].V2];

        float d1 = Cross2(a.X, a.Z, b.X, b.Z, px, pz);
        float d2 = Cross2(b.X, b.Z, c.X, c.Z, px, pz);
        float d3 = Cross2(c.X, c.Z, a.X, a.Z, px, pz);

        bool hasNeg = d1 < -Eps || d2 < -Eps || d3 < -Eps;
        bool hasPos = d1 > Eps || d2 > Eps || d3 > Eps;
        return !(hasNeg && hasPos);
    }

    // 2D cross product of (b-a) x (p-a).
    private static float Cross2(float ax, float az, float bx, float bz, float px, float pz)
        => (bx - ax) * (pz - az) - (bz - az) * (px - ax);
}
