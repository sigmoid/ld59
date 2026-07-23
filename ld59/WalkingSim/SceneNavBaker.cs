using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Quartz;
using NavMeshBaker;

namespace ld59.WalkingSim;

// In-process navmesh baking for the in-game editor. Gathers walkable geometry straight from the
// live scene's Mesh3D entities that carry a NavMeshObstacleComponent (explicit opt-in -- an
// entity with no geometry-shape marker never needs a "don't collide" flag to be excluded), runs
// Recast via the shared NavMeshBake, and returns a runtime NavMesh. Optionally writes the baked
// detail mesh to an OBJ so the existing Scene3DAsset.NavMeshPath flow keeps working. No Blender,
// no FBX round-trip: the geometry it bakes is exactly what the game renders, so visual and
// navmesh can't diverge.
public static class SceneNavBaker
{
    public sealed class Result
    {
        public NavMesh      NavMesh;      // for the walker + debug overlay
        public TriangleSoup NavSoup;      // baked detail mesh (for OBJ persistence)
        public int          SourceTris;   // input triangle count (diagnostics)
    }

    // MAIN-THREAD ONLY: reads model vertex/index buffers off the GPU (GetData), which isn't
    // thread-safe, so this must run on the thread that owns the GraphicsDevice. Produces a plain
    // CPU triangle soup that BakeFromSoup can then chew on from a background thread.
    public static TriangleSoup GatherSoup(Scene scene, float sceneScale, out int sourceTris)
    {
        var verts = new List<float>();
        var tris  = new List<int>();

        foreach (var entity in scene.FindEntitiesWithComponent<NavMeshObstacleComponent>())
        {
            if (!entity.Visible) continue;

            var mesh = entity.GetComponent<Mesh3DComponent>();
            if (mesh == null || mesh.NoCollide || mesh.Model == null) continue;

            var world = mesh.WorldMatrix;
            if (sceneScale != 1f) world *= Matrix.CreateScale(sceneScale);
            try
            {
                ModelGeometry.GatherWorldTriangles(mesh.Model, world, verts, tris);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SceneNavBaker] skipped {entity.Name}: {ex.Message}");
            }
        }

        sourceTris = tris.Count / 3;
        if (sourceTris == 0)
            throw new InvalidOperationException(
                "No walkable geometry found. Tag at least one Mesh3D entity with " +
                "NavMeshObstacle (and make sure it isn't NoCollide).");

        return new TriangleSoup { Verts = verts.ToArray(), Tris = tris.ToArray() };
    }

    // Pure CPU + no GraphicsDevice access, so it's safe to call from a background thread. The
    // returned NavMesh is likewise CPU-only; only the debug overlay's GPU buffers (built by the
    // caller from the returned NavMesh) have to go back on the main thread.
    public static Result BakeFromSoup(TriangleSoup input, BakeParams p, int sourceTris)
    {
        GuardExtent(input.Verts, p);
        var navSoup = NavMeshBake.Build(input, p);
        var navMesh = ToNavMesh(navSoup);
        return new Result { NavMesh = navMesh, NavSoup = navSoup, SourceTris = sourceTris };
    }

    // Synchronous convenience (gather + bake in one call, all on the current thread).
    public static Result Bake(Scene scene, float sceneScale, BakeParams p)
    {
        var soup = GatherSoup(scene, sceneScale, out int sourceTris);
        return BakeFromSoup(soup, p, sourceTris);
    }

    // Recast voxelizes the whole XZ bounding box at CellSize, allocating a width*height cell grid.
    // A very large extent -- a distant-scenery mesh tagged as an obstacle, or geometry left at
    // Blender's cm scale -- makes that grid huge; past ~46000 cells on an axis the int width*height
    // overflows and DotRecast throws the opaque "Arithmetic operation resulted in an overflow."
    // Fail early with numbers that point at the actual cause instead.
    private const int MaxGridAxisCells = 8192;   // ~1.2 km per axis at the default 0.15 m cell
    private const long MaxGridCells    = 30_000_000L;

    private static void GuardExtent(float[] verts, BakeParams p)
    {
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < verts.Length; i += 3)
        {
            float x = verts[i], z = verts[i + 2];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }

        float extentX = maxX - minX, extentZ = maxZ - minZ;
        double gridW = extentX / p.CellSize, gridH = extentZ / p.CellSize;

        if (gridW > MaxGridAxisCells || gridH > MaxGridAxisCells || gridW * gridH > MaxGridCells)
            throw new InvalidOperationException(
                $"Tagged geometry is too large to bake: {extentX:0} x {extentZ:0} units would need a " +
                $"{gridW:0} x {gridH:0} voxel grid at cell size {p.CellSize} m. This usually means a " +
                $"large background mesh (distant terrain/skybox) is tagged as a NavMeshObstacle -- " +
                $"untag it (you don't walk on it), or bake only the near/ground meshes. If the level " +
                $"really is this big, raise the bake cell size for a coarser navmesh.");
    }

    // Write the baked detail mesh as the navmesh OBJ the game loads (welds coincident verts so
    // shared edges read as walkable connections, matching the offline baker's output).
    public static void WriteObj(TriangleSoup soup, string path)
    {
        using var w = new StreamWriter(path);
        ObjWriter.Write(w, soup);
    }

    private static NavMesh ToNavMesh(TriangleSoup soup)
    {
        var verts = new Vector3[soup.VertexCount];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new Vector3(soup.Verts[i * 3 + 0], soup.Verts[i * 3 + 1], soup.Verts[i * 3 + 2]);

        var faces = new List<(int, int, int)>(soup.TriangleCount);
        for (int t = 0; t < soup.TriangleCount; t++)
            faces.Add((soup.Tris[t * 3 + 0], soup.Tris[t * 3 + 1], soup.Tris[t * 3 + 2]));

        return NavMesh.Build(verts, faces);
    }
}
