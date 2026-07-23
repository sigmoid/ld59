using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace NavMeshBaker;

public struct BakeParams
{
    public float AgentRadius;    // meters — walkable area is inset by this, so the runtime player stays a point
    public float AgentHeight;    // meters — surfaces with less headroom above are not walkable
    public float AgentMaxClimb;  // meters — step height the agent can climb (must exceed stair rise)
    public float AgentMaxSlope;  // degrees — steeper faces are walls

    public float CellSize;       // meters — voxel size in XZ; smaller = more accurate + slower
    public float CellHeight;     // meters — voxel size in Y

    public static BakeParams Default => new BakeParams
    {
        AgentRadius = 0.35f,
        AgentHeight = 1.8f,
        AgentMaxClimb = 0.3f,
        AgentMaxSlope = 45f,
        CellSize = 0.15f,
        CellHeight = 0.15f,
    };
}

// Wraps DotRecast: rasterizes a triangle soup into a walkable navmesh and returns the Recast
// detail mesh (already triangles, terrain-following) as a flat TriangleSoup.
public static class NavMeshBake
{
    // Standard Recast "walkable" area value (RC_WALKABLE_AREA); any non-zero area marks a span walkable.
    private const int WalkableArea = 63;

    public static TriangleSoup Build(TriangleSoup geom, BakeParams p)
    {
        if (geom.TriangleCount == 0)
            throw new System.InvalidOperationException("Input geometry has no triangles.");

        var provider = new RcSampleInputGeomProvider(geom.Verts, geom.Tris);

        var cfg = new RcConfig(
            RcPartition.WATERSHED,
            p.CellSize, p.CellHeight,
            p.AgentMaxSlope, p.AgentHeight, p.AgentRadius, p.AgentMaxClimb,
            regionMinSize: 8, regionMergeSize: 20,
            edgeMaxLen: 12f, edgeMaxError: 1.3f,
            vertsPerPoly: 6,
            detailSampleDist: 6f, detailSampleMaxError: 1f,
            filterLowHangingObstacles: true, filterLedgeSpans: true, filterWalkableLowHeightSpans: true,
            walkableAreaMod: new RcAreaModification(WalkableArea),
            buildMeshDetail: true);

        var bcfg = new RcBuilderConfig(cfg, provider.GetMeshBoundsMin(), provider.GetMeshBoundsMax());
        var result = new RcBuilder().Build(provider, bcfg, keepInterResults: false);

        var dmesh = result.MeshDetail;
        if (dmesh == null || dmesh.ntris == 0)
            throw new System.InvalidOperationException(
                "Recast produced an empty navmesh. Check that geometry is walkable (slope/step/agent params) and units are meters.");

        return FromDetailMesh(dmesh);
    }

    // RcPolyMeshDetail packs sub-meshes: meshes[i*4] = base vertex, +1 = vert count,
    // +2 = base triangle, +3 = triangle count. verts are world-space xyz. tris are 4 ints each
    // (3 vertex indices LOCAL to the sub-mesh base + one flags int).
    private static TriangleSoup FromDetailMesh(RcPolyMeshDetail dmesh)
    {
        var verts = new System.Collections.Generic.List<float>();
        var tris = new System.Collections.Generic.List<int>();

        for (int i = 0; i < dmesh.nmeshes; i++)
        {
            int baseVert = dmesh.meshes[i * 4 + 0];
            int baseTri  = dmesh.meshes[i * 4 + 2];
            int triCount = dmesh.meshes[i * 4 + 3];

            for (int t = 0; t < triCount; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int local = dmesh.tris[(baseTri + t) * 4 + k];
                    int global = baseVert + local;
                    tris.Add(verts.Count / 3);
                    verts.Add(dmesh.verts[global * 3 + 0]);
                    verts.Add(dmesh.verts[global * 3 + 1]);
                    verts.Add(dmesh.verts[global * 3 + 2]);
                }
            }
        }

        return new TriangleSoup { Verts = verts.ToArray(), Tris = tris.ToArray() };
    }
}
