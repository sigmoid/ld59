using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ld59.WalkingSim;
using Microsoft.Xna.Framework;

namespace NavMeshBaker;

// Offline navmesh baker. Reads level geometry (an OBJ, or the BoxPrimitive3D entities of a scene
// XML), runs Recast, and writes the navmesh OBJ the game loads via Scene3DAsset.NavMeshPath.
internal static class Program
{
    private static int Main(string[] args)
    {
        var positional = new List<string>();
        var p = BakeParams.Default;
        bool validate = false;
        float scale = 1f;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--radius": p.AgentRadius = Arg(args, ++i); break;
                    case "--height": p.AgentHeight = Arg(args, ++i); break;
                    case "--step":   p.AgentMaxClimb = Arg(args, ++i); break;
                    case "--slope":  p.AgentMaxSlope = Arg(args, ++i); break;
                    case "--cell":   p.CellSize = Arg(args, ++i); break;
                    case "--cellHeight": p.CellHeight = Arg(args, ++i); break;
                    case "--scale":  scale = Arg(args, ++i); break;
                    case "--validate": validate = true; break;
                    default: positional.Add(args[i]); break;
                }
            }
        }
        catch (Exception)
        {
            return Usage();
        }

        if (positional.Count != 2) return Usage();
        string input = positional[0], output = positional[1];

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input not found: {input}");
            return 1;
        }

        // Gather triangles by input type.
        TriangleSoup geom;
        string ext = Path.GetExtension(input).ToLowerInvariant();
        if (ext == ".obj")
        {
            using var r = new StreamReader(File.OpenRead(input));
            geom = TriangleSoup.FromObj(r);
        }
        else if (ext == ".xml")
        {
            geom = TriangleSoup.FromBoxScene(input);
        }
        else
        {
            Console.Error.WriteLine($"Unsupported input extension '{ext}' (expected .obj or .xml).");
            return 1;
        }

        // Pre-scale the input geometry (agent params stay absolute, so the player keeps its
        // real-world size relative to a larger/smaller level).
        if (scale != 1f)
        {
            for (int i = 0; i < geom.Verts.Length; i++) geom.Verts[i] *= scale;
            Console.WriteLine($"Scaled input geometry by {scale}x");
        }

        Console.WriteLine($"Input: {geom.VertexCount} verts, {geom.TriangleCount} tris");
        Console.WriteLine($"Agent: radius={p.AgentRadius} height={p.AgentHeight} step={p.AgentMaxClimb} slope={p.AgentMaxSlope}  cell={p.CellSize}/{p.CellHeight}");

        TriangleSoup nav;
        try
        {
            nav = NavMeshBake.Build(geom, p);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Bake failed: {ex.Message}");
            return 1;
        }

        using (var w = new StreamWriter(File.Create(output)))
            ObjWriter.Write(w, nav);
        Console.WriteLine($"Wrote {output}: {nav.TriangleCount} navmesh tris");

        if (validate) return Validate(output);
        return 0;
    }

    // Load the output back through the runtime NavMesh and sanity-check connectivity.
    private static int Validate(string objPath)
    {
        NavMesh mesh;
        using (var r = new StreamReader(File.OpenRead(objPath)))
            mesh = ObjParser.LoadNavMesh(r);

        int boundary = 0, shared = 0;
        foreach (var t in mesh.Triangles)
        {
            foreach (int n in new[] { t.N01, t.N12, t.N20 })
                if (n < 0) boundary++; else shared++;
        }
        Console.WriteLine($"Validate: {mesh.Vertices.Length} verts, {mesh.Triangles.Length} tris, {shared / 2} shared edges, {boundary} boundary edges");

        if (mesh.Triangles.Length == 0)
        {
            Console.Error.WriteLine("Validate FAILED: navmesh is empty.");
            return 1;
        }

        // confirm a point over the first triangle's centroid resolves
        var t0 = mesh.Triangles[0];
        var c = (mesh.Vertices[t0.V0] + mesh.Vertices[t0.V1] + mesh.Vertices[t0.V2]) / 3f;
        int found = mesh.FindTriangle(new Vector3(c.X, c.Y + 0.1f, c.Z));
        Console.WriteLine(found >= 0 ? "Validate OK: sample point resolves." : "Validate WARN: sample point did not resolve.");
        return 0;
    }

    private static float Arg(string[] args, int i)
    {
        if (i >= args.Length) throw new ArgumentException();
        return float.Parse(args[i], CultureInfo.InvariantCulture);
    }

    private static int Usage()
    {
        Console.WriteLine(
            "NavMeshBaker — generate a walking-sim navmesh with Recast.\n\n" +
            "  dotnet run  <input.obj | scene.xml>  <output.obj>  [options]\n\n" +
            "Options (meters/degrees):\n" +
            "  --radius <m>      agent radius / wall inset (default 0.35)\n" +
            "  --height <m>      agent height / min headroom (default 1.8)\n" +
            "  --step <m>        max step climb (default 0.3)\n" +
            "  --slope <deg>     max walkable slope (default 45)\n" +
            "  --cell <m>        XZ voxel size (default 0.15)\n" +
            "  --cellHeight <m>  Y voxel size (default 0.15)\n" +
            "  --scale <f>       pre-scale input geometry (e.g. 4 for a 4x-bigger level)\n" +
            "  --validate        load the result back and report connectivity\n");
        return 1;
    }
}
