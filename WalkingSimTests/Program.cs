using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using ld59.WalkingSim;

// Headless assertions for the walking-sim navmesh runtime: OBJ parsing, vertex weld +
// adjacency, winding-agnostic point/height math, and the edge-walk mover (interior,
// edge-crossing, boundary slide, corner exits, fuzz, and overlapping surfaces).
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(string desc, bool cond)
    {
        if (cond) { _passed++; Console.WriteLine($"  PASS  {desc}"); }
        else { _failed++; Console.WriteLine($"  FAIL  {desc}"); }
    }

    private static int Main(string[] args)
    {
        Console.WriteLine("Walking-sim navmesh tests\n");

        ObjParsing();
        WeldAndAdjacency();
        PointInTriangle();
        PlaneHeight();
        MoveInterior();
        MoveCrossEdge();
        MoveBoundarySlide();
        CornerExit();
        CornerRounding();
        EscapeFuzz();
        OverlapTunnel();
        RoofOverhang();
        DemoLevel();
        BakedLevel();
        TerrainLevel();

        Console.WriteLine($"\n{_passed}/{_passed + _failed} passed.");
        return _failed == 0 ? 0 : 1;
    }

    // Walk the demo controller in a straight compass direction for a while, returning where it
    // ends up. Uses many small steps so the edge-walk crosses triangles naturally.
    private static Vector3 WalkDir(WalkController w, Vector2 dir, float seconds)
    {
        for (float t = 0; t < seconds; t += 1f / 60f)
            w.Move(dir, 1f / 60f);
        return w.Position;
    }

    // ── Demo level: load the real walk_level_navmesh.obj and traverse it ──────────────

    private static void DemoLevel()
    {
        Console.WriteLine("Demo level (walk_level_navmesh.obj)");

        string path = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "ld59", "Content", "files", "scenes", "walk_level_navmesh.obj");
        if (!File.Exists(path))
        {
            Check("demo navmesh file exists", false);
            return;
        }

        NavMesh mesh;
        using (var r = new StreamReader(File.OpenRead(path)))
            mesh = ObjParser.LoadNavMesh(r);

        Check("demo mesh welds to 14 vertices", mesh.Vertices.Length == 14);
        Check("demo mesh has 12 triangles", mesh.Triangles.Length == 12);

        // spawn at the scene's PlayerStart (3,0,3)
        var courtyard = new WalkController(mesh) { MoveSpeed = 3f };
        Check("spawn at PlayerStart succeeds", courtyard.Spawn(new Vector3(3, 0, 3)));
        Check("spawn floor height is 0", Near(courtyard.Position.Y, 0f, 0.01f));

        // walk north (+Z) toward the stairs at x~9, then up onto the platform (y=2)
        var toStairs = new WalkController(mesh) { MoveSpeed = 4f };
        toStairs.Spawn(new Vector3(9, 0, 3));
        var top = WalkDir(toStairs, new Vector2(0, 1), 6f);
        Check("ramp path climbs onto the platform (y~2)", top.Y > 1.5f);
        Check("ramp path stays on mesh", mesh.FindTriangle(top) >= 0);

        // walk north (+Z) through the corridor at x~3, passing UNDER the platform (stays y=0)
        var toTunnel = new WalkController(mesh) { MoveSpeed = 4f };
        toTunnel.Spawn(new Vector3(3, 0, 3));
        var underneath = WalkDir(toTunnel, new Vector2(0, 1), 6f);
        Check("tunnel path reaches under the platform (z>14)", underneath.Z > 14f);
        Check("tunnel path stays at floor level (y~0)", Near(underneath.Y, 0f, 0.05f));
        Check("tunnel path stays on mesh", mesh.FindTriangle(underneath) >= 0);
    }

    private static bool Near(float a, float b, float eps = 1e-3f) => MathF.Abs(a - b) <= eps;

    // A unit square on the XZ plane at y=0, split into two triangles sharing the diagonal.
    // Verts: 0=(0,0,0) 1=(1,0,0) 2=(1,0,1) 3=(0,0,1)
    private static NavMesh UnitSquare()
    {
        var verts = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0),
            new Vector3(1, 0, 1), new Vector3(0, 0, 1),
        };
        var faces = new List<(int, int, int)> { (0, 1, 2), (0, 2, 3) };
        return NavMesh.Build(verts, faces);
    }

    // ── OBJ parsing ──────────────────────────────────────────────────────────────

    private static void ObjParsing()
    {
        Console.WriteLine("OBJ parsing");

        var (v, f) = ObjParser.Parse(new StringReader(
            "v 0 0 0\nv 1 0 0\nv 1 0 1\nf 1 2 3\n"));
        Check("triangle: 3 verts, 1 face", v.Count == 3 && f.Count == 1);
        Check("triangle: 1-based -> 0-based", f[0] == (0, 1, 2));

        var (_, f2) = ObjParser.Parse(new StringReader(
            "v 0 0 0\nv 1 0 0\nv 1 0 1\nf 1/1/1 2/2/2 3/3/3\n"));
        Check("face tokens i/j/k use vertex index", f2.Count == 1 && f2[0] == (0, 1, 2));

        var (_, f3) = ObjParser.Parse(new StringReader(
            "v 0 0 0\nv 1 0 0\nv 1 0 1\nv 0 0 1\nf 1 2 3 4\n"));
        Check("quad fan-triangulates to 2 tris", f3.Count == 2 && f3[0] == (0, 1, 2) && f3[1] == (0, 2, 3));

        var (v4, f4) = ObjParser.Parse(new StringReader(
            "# comment\nvt 0 0\nvn 0 1 0\nv 0 0 0\nv 1 0 0\nv 1 0 1\nf 1//1 2//1 3//1\n"));
        Check("ignores comment/vt/vn lines", v4.Count == 3 && f4.Count == 1);
        Check("face token i//k uses vertex index", f4[0] == (0, 1, 2));
    }

    // ── Weld + adjacency ───────────────────────────────────────────────────────────

    private static void WeldAndAdjacency()
    {
        Console.WriteLine("Weld + adjacency");

        // two triangles written with independent (near-duplicate) shared-edge verts
        var verts = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1),   // tri A
            new Vector3(0.000005f, 0, 0), new Vector3(1, 0, 1.000004f), new Vector3(0, 0, 1), // tri B (dup 0 and 2)
        };
        var faces = new List<(int, int, int)> { (0, 1, 2), (3, 4, 5) };
        var mesh = NavMesh.Build(verts, faces);

        Check("near-duplicate verts weld to 4", mesh.Vertices.Length == 4);
        Check("both faces survive", mesh.Triangles.Length == 2);

        // the shared edge (welded 0-2 diagonal) must link both triangles
        int links0 = CountNeighbours(mesh.Triangles[0]);
        int links1 = CountNeighbours(mesh.Triangles[1]);
        Check("tri A has exactly one neighbour", links0 == 1);
        Check("tri B has exactly one neighbour", links1 == 1);

        // outer edges are boundaries (-1)
        int boundaries = (3 - links0) + (3 - links1);
        Check("outer edges are boundaries", boundaries == 4);
    }

    private static int CountNeighbours(NavMesh.NavTriangle t)
    {
        int c = 0;
        if (t.N01 >= 0) c++;
        if (t.N12 >= 0) c++;
        if (t.N20 >= 0) c++;
        return c;
    }

    // ── Point in triangle (winding-agnostic) ────────────────────────────────────────

    private static void PointInTriangle()
    {
        Console.WriteLine("Point in triangle");

        var ccw = UnitSquare();
        Check("inside CCW mesh", ccw.FindTriangle(new Vector3(0.5f, 0, 0.25f)) >= 0);
        Check("outside CCW mesh", ccw.FindTriangle(new Vector3(2f, 0, 2f)) < 0);
        Check("on shared diagonal is inside", ccw.FindTriangle(new Vector3(0.5f, 0, 0.5f)) >= 0);

        // clockwise winding of the same geometry must behave identically
        var cwVerts = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0),
            new Vector3(1, 0, 1), new Vector3(0, 0, 1),
        };
        var cwFaces = new List<(int, int, int)> { (2, 1, 0), (3, 2, 0) };
        var cw = NavMesh.Build(cwVerts, cwFaces);
        Check("inside CW mesh (winding-agnostic)", cw.FindTriangle(new Vector3(0.5f, 0, 0.25f)) >= 0);
        Check("outside CW mesh (winding-agnostic)", cw.FindTriangle(new Vector3(-1f, 0, 0.5f)) < 0);
    }

    // ── Plane height ───────────────────────────────────────────────────────────────

    private static void PlaneHeight()
    {
        Console.WriteLine("Plane height");

        // single triangle sloping up +1 in Y per +1 in X: A=(0,0,0) B=(2,2,0) C=(0,0,2)
        var verts = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(2, 2, 0), new Vector3(0, 0, 2) };
        var up = NavMesh.Build(verts, new List<(int, int, int)> { (0, 1, 2) });
        Check("height at A", Near(up.HeightAt(0, 0, 0), 0));
        Check("height at x=1", Near(up.HeightAt(0, 1, 0.2f), 1f));
        Check("height at x=2", Near(up.HeightAt(0, 2, 0), 2f));

        // reversed winding must give the same heights
        var down = NavMesh.Build(verts, new List<(int, int, int)> { (2, 1, 0) });
        Check("height winding-agnostic", Near(down.HeightAt(0, 1, 0.2f), 1f));
    }

    // ── Move: interior ─────────────────────────────────────────────────────────────

    private static void MoveInterior()
    {
        Console.WriteLine("Move: interior");

        var mesh = UnitSquare();
        int tri = mesh.FindTriangle(new Vector3(0.5f, 0, 0.25f));
        var (pos, newTri) = mesh.Move(tri, new Vector3(0.5f, 0, 0.25f), new Vector2(0.1f, 0.05f));
        Check("interior move lands exactly", Near(pos.X, 0.6f) && Near(pos.Z, 0.3f));
        Check("interior move stays in mesh", newTri >= 0);
        Check("interior move Y on plane", Near(pos.Y, 0f));
    }

    // ── Move: cross a shared edge onto a higher neighbour ────────────────────────────

    private static void MoveCrossEdge()
    {
        Console.WriteLine("Move: cross edge");

        // two triangles meeting along x=1: left flat at y=0, right sloping up to y=1 at x=2
        var verts = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 2), new Vector3(0, 0, 2), // left quad-ish
            new Vector3(2, 1, 0), new Vector3(2, 1, 2),
        };
        var faces = new List<(int, int, int)>
        {
            (0, 1, 2), (0, 2, 3),   // left, y=0
            (1, 4, 5), (1, 5, 2),   // right, rising to y=1
        };
        var mesh = NavMesh.Build(verts, faces);

        int start = mesh.FindTriangle(new Vector3(0.5f, 0, 1f));
        var (pos, tri) = mesh.Move(start, new Vector3(0.5f, 0, 1f), new Vector2(1.0f, 0f));
        Check("crossed onto neighbour triangle", tri != start && tri >= 0);
        Check("moved past the shared edge", pos.X > 1f);
        Check("Y rose onto the slope", pos.Y > 0.1f);
    }

    // ── Move: boundary slide ─────────────────────────────────────────────────────────

    private static void MoveBoundarySlide()
    {
        Console.WriteLine("Move: boundary slide");

        var mesh = UnitSquare();
        // stand near the +X wall, push diagonally into it (+X and +Z). X is blocked, Z slides.
        var start = new Vector3(0.9f, 0, 0.5f);
        int tri = mesh.FindTriangle(start);
        var (pos, newTri) = mesh.Move(tri, start, new Vector2(0.5f, 0.3f));

        Check("stayed on mesh after slide", newTri >= 0 && mesh.FindTriangle(pos) >= 0);
        Check("blocked on X (did not leave +X wall)", pos.X <= 1f + 1e-2f);
        Check("slid along Z (tangential preserved)", pos.Z > 0.5f);
    }

    // ── Corner exit ──────────────────────────────────────────────────────────────────

    private static void CornerExit()
    {
        Console.WriteLine("Corner exit");

        var mesh = UnitSquare();
        // aim straight at the (1,0,1) corner shared by both triangles and the outer walls
        var start = new Vector3(0.5f, 0, 0.5f);
        int tri = mesh.FindTriangle(start);
        var (pos, newTri) = mesh.Move(tri, start, new Vector2(1.0f, 1.0f));
        Check("corner move terminates on mesh", newTri >= 0);
        Check("corner move no NaN", !float.IsNaN(pos.X) && !float.IsNaN(pos.Y) && !float.IsNaN(pos.Z));
        Check("corner move stays in bounds", pos.X <= 1f + 1e-2f && pos.Z <= 1f + 1e-2f);
    }

    // ── Corner rounding: the walker must not get pinned while sliding along a wall ─────
    // Real Recast-baked meshes have irregular wall vertices and T-junctions (an adjacent walkable
    // triangle meeting an edge without a welded/matching edge, so it has no neighbour link and
    // looks like a wall). Both used to pin the walker while it slid along a wall -- the user-visible
    // "stuck on a corner / bump". Synthetic axis-aligned meshes never reproduce it, so we fuzz the
    // real baked meshes for BUMP catches: the walker slid somewhere, ended pinned, yet walkable
    // floor genuinely continues just past the stop. Legitimate convex apices and head-on walls
    // (nothing on-mesh past the stop) are excluded.
    private static void CornerRounding()
    {
        Console.WriteLine("Corner rounding (baked-mesh slides)");

        // empty_level's navmesh ships in the repo; the walk-level baked mesh only exists in the
        // game build output. Test whichever are present.
        var meshes = new List<(string name, string path)>
        {
            ("empty_level", Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ld59",
                "Content", "files", "scenes", "empty_level_navmesh.obj")),
            ("walk_level_baked", Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ld59",
                "bin", "Debug", "net9.0-windows", "Content", "files", "scenes", "walk_level_navmesh_baked.obj")),
        };

        bool anyRan = false;
        foreach (var (name, path) in meshes)
        {
            if (!File.Exists(path)) continue;
            anyRan = true;
            NavMesh mesh;
            using (var r = new StreamReader(File.OpenRead(path))) mesh = ObjParser.LoadNavMesh(r);

            var rng = new Random(4242);
            int bumps = 0;
            for (int trial = 0; trial < 4000; trial++)
            {
                var w = new WalkController(mesh) { MoveSpeed = 6f };
                var tr = mesh.Triangles[rng.Next(mesh.Triangles.Length)];
                var a = mesh.Vertices[tr.V0]; var b = mesh.Vertices[tr.V1]; var c = mesh.Vertices[tr.V2];
                if (!w.Spawn(new Vector3((a.X + b.X + c.X) / 3f, (a.Y + b.Y + c.Y) / 3f, (a.Z + b.Z + c.Z) / 3f))) continue;

                var dir = new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1));
                if (dir.Length() < 1e-3f) continue;

                var prev = new Vector2(w.Position.X, w.Position.Z);
                float traveled = 0; int tailStuck = 0;
                for (int i = 0; i < 200; i++)
                {
                    w.Move(dir, 1f / 60f);
                    var p = new Vector2(w.Position.X, w.Position.Z);
                    traveled += (p - prev).Length();
                    if (i >= 170 && (p - prev).Length() < 0.002f) tailStuck++;
                    prev = p;
                }

                // A BUMP bug = the walker really slid somewhere, then ended pinned, yet walkable floor
                // genuinely continues just past the stop along the same input. (A legitimate convex
                // apex or a wall pressed head-on has nothing on-mesh ahead and is excluded.)
                if (tailStuck >= 28 && traveled > 1.0f)
                {
                    var du = dir; du /= du.Length();
                    var ahead = new Vector3(w.Position.X + du.X * 0.3f, w.Position.Y, w.Position.Z + du.Y * 0.3f);
                    if (mesh.FindTriangle(ahead) < 0) continue;   // nothing on-mesh past the stop -> not a bump
                    var w2 = new WalkController(mesh) { MoveSpeed = 6f };
                    if (w2.Spawn(ahead))
                    {
                        var b0 = new Vector2(w2.Position.X, w2.Position.Z);
                        for (int k = 0; k < 40; k++) w2.Move(dir, 1f / 60f);
                        if ((new Vector2(w2.Position.X, w2.Position.Z) - b0).Length() > 0.5f) bumps++;
                    }
                }
            }
            Check($"no bump-stuck spots sliding on {name}", bumps == 0);
        }

        if (!anyRan) Check("a baked navmesh was present for the corner test (skipped)", true);
    }

    // ── Escape fuzz ──────────────────────────────────────────────────────────────────

    private static void EscapeFuzz()
    {
        Console.WriteLine("Escape fuzz");

        var mesh = UnitSquare();
        var rng = new Random(12345);
        var pos = new Vector3(0.5f, 0, 0.5f);
        int tri = mesh.FindTriangle(pos);
        bool everEscaped = false;
        bool everNaN = false;

        for (int i = 0; i < 2000; i++)
        {
            var delta = new Vector2((float)(rng.NextDouble() * 0.4 - 0.2), (float)(rng.NextDouble() * 0.4 - 0.2));
            (pos, tri) = mesh.Move(tri, pos, delta);
            if (tri < 0 || mesh.FindTriangle(pos) < 0) everEscaped = true;
            if (float.IsNaN(pos.X) || float.IsNaN(pos.Z) || float.IsNaN(pos.Y)) everNaN = true;
        }

        Check("2000 random moves never left the mesh", !everEscaped);
        Check("2000 random moves never produced NaN", !everNaN);
    }

    // ── Overlap: tunnel under a platform ──────────────────────────────────────────────

    private static void OverlapTunnel()
    {
        Console.WriteLine("Overlap: tunnel under platform");

        // two disconnected floors overlapping in XZ, 3 m apart in Y
        var lower = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 2), new Vector3(0, 0, 2) };
        var upper = new List<Vector3> { new Vector3(0, 3, 0), new Vector3(2, 3, 0), new Vector3(2, 3, 2), new Vector3(0, 3, 2) };
        var verts = new List<Vector3>();
        verts.AddRange(lower);
        verts.AddRange(upper);
        var faces = new List<(int, int, int)>
        {
            (0, 1, 2), (0, 2, 3),   // lower floor
            (4, 5, 6), (4, 6, 7),   // upper floor
        };
        var mesh = NavMesh.Build(verts, faces);

        int lowerTri = mesh.FindTriangle(new Vector3(1f, 0f, 1f));
        int upperTri = mesh.FindTriangle(new Vector3(1f, 3f, 1f));
        Check("lower query finds a y=0 triangle", lowerTri >= 0 && Near(mesh.HeightAt(lowerTri, 1f, 1f), 0f));
        Check("upper query finds a y=3 triangle", upperTri >= 0 && Near(mesh.HeightAt(upperTri, 1f, 1f), 3f));
        Check("overlapping surfaces are distinct", lowerTri != upperTri);

        // a walker on the lower floor must never surface onto the upper one
        var walker = new WalkController(mesh);
        walker.Spawn(new Vector3(1f, 0f, 1f));
        bool stayedLow = true;
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            walker.Move(new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)), 0.1f);
            if (walker.Position.Y > 1.5f) stayedLow = false;
        }
        Check("walker on lower floor never surfaces to upper", stayedLow);
    }

    // ── Overhang: a roof directly above a floor's boundary wall ───────────────────────
    // Regression for the "walk to an edge inside the house and teleport onto the roof" bug. When
    // the walker slides into a boundary wall, the mover probes just across the edge for a T-junction
    // continuation. If a roof triangle overhangs past that wall it is the only thing overlapping the
    // probe point in XZ, so FindTriangle used to hand it back and the walker "crossed" up onto it.
    // The floor and roof share no vertices, so the roof is never a real neighbour -- the walker must
    // stay on the floor and slide along the wall.
    private static void RoofOverhang()
    {
        Console.WriteLine("Overhang: roof above a boundary wall");

        // floor: X in [0,1], Z in [0,2] at y=0.  roof: X in [0,2], Z in [0,2] at y=3 -- it extends
        // past the floor's +X wall (X=1), so just outside that wall only the roof exists in XZ.
        var verts = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 2), new Vector3(0, 0, 2), // floor
            new Vector3(0, 3, 0), new Vector3(2, 3, 0), new Vector3(2, 3, 2), new Vector3(0, 3, 2), // roof (overhangs +X)
        };
        var faces = new List<(int, int, int)>
        {
            (0, 1, 2), (0, 2, 3),   // floor, y=0
            (4, 5, 6), (4, 6, 7),   // roof, y=3
        };
        var mesh = NavMesh.Build(verts, faces);

        var walker = new WalkController(mesh);
        Check("spawn on floor succeeds", walker.Spawn(new Vector3(0.5f, 0, 1f)));

        // push straight into the +X wall for a while; the walker must ride the wall, not the roof.
        bool everRoof = false;
        for (int i = 0; i < 400; i++)
        {
            walker.Move(new Vector2(1f, 0f), 1f / 60f);
            if (walker.Position.Y > 1.5f) everRoof = true;
        }
        Check("walking into the wall never teleports onto the roof", !everRoof);
        Check("stayed at floor height (y~0)", Near(walker.Position.Y, 0f, 0.05f));
        Check("stayed on mesh", mesh.FindTriangle(walker.Position) >= 0);

        // and a diagonal push (slides along the wall) must likewise stay low
        walker.Spawn(new Vector3(0.5f, 0, 0.5f));
        everRoof = false;
        for (int i = 0; i < 400; i++)
        {
            walker.Move(new Vector2(1f, 0.6f), 1f / 60f);
            if (walker.Position.Y > 1.5f) everRoof = true;
        }
        Check("diagonal slide into the wall never surfaces to the roof", !everRoof);
    }

    // ── Baked level: load the Recast-generated navmesh and traverse it ─────────────────
    // (walk_level_navmesh_baked.obj is produced by NavMeshBaker from walk_level.xml's boxes.)

    private static void BakedLevel()
    {
        Console.WriteLine("Baked level (walk_level_navmesh_baked.obj)");

        string path = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "ld59", "Content", "files", "scenes", "walk_level_navmesh_baked.obj");
        if (!File.Exists(path))
        {
            Check("baked navmesh file exists (run NavMeshBaker first)", false);
            return;
        }

        NavMesh mesh;
        using (var r = new StreamReader(File.OpenRead(path)))
            mesh = ObjParser.LoadNavMesh(r);

        Check("baked mesh is non-empty", mesh.Triangles.Length > 0);
        int shared = 0;
        foreach (var t in mesh.Triangles)
            foreach (int n in new[] { t.N01, t.N12, t.N20 }) if (n >= 0) shared++;
        Check("baked mesh is connected (has shared edges)", shared > 0);

        // spawn near the scene's PlayerStart; Recast insets from walls + quantizes height, so
        // tolerances are looser than the hand mesh.
        var courtyard = new WalkController(mesh) { MoveSpeed = 3f };
        Check("spawn near PlayerStart succeeds", courtyard.Spawn(new Vector3(3, 0.2f, 3)));
        Check("spawn floor height near 0", Near(courtyard.Position.Y, 0f, 0.4f));

        // climb the stairs onto the platform (y~2)
        var toStairs = new WalkController(mesh) { MoveSpeed = 4f };
        if (toStairs.Spawn(new Vector3(9, 0.2f, 3)))
        {
            var top = WalkDir(toStairs, new Vector2(0, 1), 6f);
            Check("baked ramp path climbs onto the platform (y>1.5)", top.Y > 1.5f);
            Check("baked ramp path stays on mesh", mesh.FindTriangle(top) >= 0);
        }
        else Check("spawn at stairs base succeeds", false);

        // walk the corridor under the platform (stays low)
        var toTunnel = new WalkController(mesh) { MoveSpeed = 4f };
        if (toTunnel.Spawn(new Vector3(3, 0.2f, 3)))
        {
            var underneath = WalkDir(toTunnel, new Vector2(0, 1), 6f);
            Check("baked tunnel path reaches under the platform (z>14)", underneath.Z > 14f);
            Check("baked tunnel path stays low (y<1)", underneath.Y < 1f);
            Check("baked tunnel path stays on mesh", mesh.FindTriangle(underneath) >= 0);
        }
        else Check("spawn at corridor succeeds", false);
    }

    // ── Terrain level: the Recast-baked navmesh from the imported LowPolyTerrain model ──

    private static void TerrainLevel()
    {
        Console.WriteLine("Terrain level (terrain_navmesh.obj)");

        string path = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "ld59", "Content", "files", "scenes", "terrain_navmesh.obj");
        if (!File.Exists(path))
        {
            Check("terrain navmesh file exists (bake LowPolyTerrain.obj first)", false);
            return;
        }

        NavMesh mesh;
        using (var r = new StreamReader(File.OpenRead(path)))
            mesh = ObjParser.LoadNavMesh(r);

        Check("terrain mesh is non-empty", mesh.Triangles.Length > 0);

        // spawn at the scene's PlayerStart and confirm it lands on the surface
        var walker = new WalkController(mesh) { MoveSpeed = 3f };
        bool spawned = walker.Spawn(new Vector3(-3.29f, 17.95f, -0.62f));
        Check("spawn at PlayerStart lands on terrain", spawned);

        if (!spawned) return;

        // Wander the surface: the walker must never leave the mesh, never NaN, and its height
        // must track the terrain's relief. A high move speed makes the walk range widely enough
        // to cross elevation changes rather than jitter in place near spawn.
        walker.MoveSpeed = 10f;
        var rng = new Random(99);
        bool everOff = false, everNaN = false;
        float minY = walker.Position.Y, maxY = walker.Position.Y;
        for (int i = 0; i < 4000; i++)
        {
            var dir = new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1));
            walker.Move(dir, 1f / 30f);
            if (mesh.FindTriangle(walker.Position) < 0) everOff = true;
            if (float.IsNaN(walker.Position.Y)) everNaN = true;
            minY = MathF.Min(minY, walker.Position.Y);
            maxY = MathF.Max(maxY, walker.Position.Y);
        }
        Check("terrain walk never leaves the mesh", !everOff);
        Check("terrain walk never NaNs", !everNaN);
        Check("terrain walk follows height changes", maxY - minY > 0.5f); // relief, so Y varies
    }
}
