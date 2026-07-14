using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;

namespace ld59.WalkingSim;

// Minimal Wavefront OBJ reader for navmesh geometry. Reads `v` and `f` lines only; face
// tokens may be i, i/j, i/j/k or i//k (only the vertex index is used); faces with 4+ verts
// are fan-triangulated. Callers own the stream, so this stays free of the content pipeline
// and is unit-testable from a string.
public static class ObjParser
{
    public static (List<Vector3> verts, List<(int a, int b, int c)> faces) Parse(TextReader reader)
    {
        var verts = new List<Vector3>();
        var faces = new List<(int, int, int)>();

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var tok = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) continue;

            if (tok[0] == "v" && tok.Length >= 4)
            {
                verts.Add(new Vector3(
                    ParseFloat(tok[1]),
                    ParseFloat(tok[2]),
                    ParseFloat(tok[3])));
            }
            else if (tok[0] == "f" && tok.Length >= 4)
            {
                // collect 0-based vertex indices for this face
                int n = tok.Length - 1;
                var idx = new int[n];
                for (int i = 0; i < n; i++)
                    idx[i] = VertexIndex(tok[i + 1], verts.Count);

                // fan triangulate
                for (int i = 1; i < n - 1; i++)
                    faces.Add((idx[0], idx[i], idx[i + 1]));
            }
        }

        return (verts, faces);
    }

    public static NavMesh LoadNavMesh(TextReader reader, float weldEps = 1e-3f)
    {
        var (verts, faces) = Parse(reader);
        return NavMesh.Build(verts, faces, weldEps);
    }

    // OBJ indices are 1-based; negative indices are relative to the current vertex count.
    private static int VertexIndex(string token, int vertexCount)
    {
        int slash = token.IndexOf('/');
        string vpart = slash >= 0 ? token.Substring(0, slash) : token;
        int v = int.Parse(vpart, CultureInfo.InvariantCulture);
        return v > 0 ? v - 1 : vertexCount + v;
    }

    private static float ParseFloat(string s)
        => float.Parse(s, CultureInfo.InvariantCulture);
}
