using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ld59.WalkingSim;

namespace NavMeshBaker;

// Flat triangle geometry: Verts is xyz-interleaved, Tris are 0-based vertex indices (3 per tri).
public struct TriangleSoup
{
    public float[] Verts;
    public int[] Tris;

    public int VertexCount => Verts.Length / 3;
    public int TriangleCount => Tris.Length / 3;

    // ── Input: an OBJ file (the modeled level or a simplified collision mesh) ─────────
    public static TriangleSoup FromObj(TextReader reader)
    {
        var (verts, faces) = ObjParser.Parse(reader);
        var v = new float[verts.Count * 3];
        for (int i = 0; i < verts.Count; i++)
        {
            v[i * 3 + 0] = verts[i].X;
            v[i * 3 + 1] = verts[i].Y;
            v[i * 3 + 2] = verts[i].Z;
        }
        var t = new int[faces.Count * 3];
        for (int i = 0; i < faces.Count; i++)
        {
            t[i * 3 + 0] = faces[i].a;
            t[i * 3 + 1] = faces[i].b;
            t[i * 3 + 2] = faces[i].c;
        }
        return new TriangleSoup { Verts = v, Tris = t };
    }

    // ── Input: the BoxPrimitive3D entities of a scene XML (blockouts) ─────────────────
    // Each box is a unit cube (corners +/-0.5) scaled by its Scale and translated by Position3D
    // (BoxPrimitive3D does not apply SceneScale; walk scenes use SceneScale=1). Mesh3D entities
    // are ignored — their geometry lives in FBX and interactables aren't collision.
    public static TriangleSoup FromBoxScene(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var verts = new List<float>();
        var tris = new List<int>();

        foreach (var entity in doc.Descendants("Entity"))
        {
            var box = entity.Elements("Component")
                .FirstOrDefault(c => (string)c.Attribute("Type") == "BoxPrimitive3D");
            if (box == null) continue;

            var (sx, sy, sz) = ReadVec3(box.Elements("Property")
                .FirstOrDefault(p => (string)p.Attribute("Name") == "Scale")?.Attribute("Value")?.Value ?? "1,1,1");
            var pos = entity.Element("Position3D");
            float px = ReadFloat(pos?.Element("X")?.Value);
            float py = ReadFloat(pos?.Element("Y")?.Value);
            float pz = ReadFloat(pos?.Element("Z")?.Value);

            AppendBox(verts, tris, px, py, pz, sx, sy, sz);
        }

        return new TriangleSoup { Verts = verts.ToArray(), Tris = tris.ToArray() };
    }

    // Six quads with outward (CCW) winding; each becomes two triangles (a,b,c),(a,c,d).
    private static readonly float[][] BoxQuads =
    {
        new[] { -0.5f,-0.5f, 0.5f,  0.5f,-0.5f, 0.5f,  0.5f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f }, // +Z
        new[] {  0.5f,-0.5f,-0.5f, -0.5f,-0.5f,-0.5f, -0.5f, 0.5f,-0.5f,  0.5f, 0.5f,-0.5f }, // -Z
        new[] { -0.5f, 0.5f, 0.5f,  0.5f, 0.5f, 0.5f,  0.5f, 0.5f,-0.5f, -0.5f, 0.5f,-0.5f }, // +Y (top)
        new[] { -0.5f,-0.5f,-0.5f,  0.5f,-0.5f,-0.5f,  0.5f,-0.5f, 0.5f, -0.5f,-0.5f, 0.5f }, // -Y
        new[] {  0.5f,-0.5f, 0.5f,  0.5f,-0.5f,-0.5f,  0.5f, 0.5f,-0.5f,  0.5f, 0.5f, 0.5f }, // +X
        new[] { -0.5f,-0.5f,-0.5f, -0.5f,-0.5f, 0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,-0.5f }, // -X
    };

    private static void AppendBox(List<float> verts, List<int> tris,
        float px, float py, float pz, float sx, float sy, float sz)
    {
        foreach (var q in BoxQuads)
        {
            int b = verts.Count / 3;
            for (int i = 0; i < 4; i++)
            {
                verts.Add(q[i * 3 + 0] * sx + px);
                verts.Add(q[i * 3 + 1] * sy + py);
                verts.Add(q[i * 3 + 2] * sz + pz);
            }
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }
    }

    private static (float, float, float) ReadVec3(string s)
    {
        var parts = s.Split(',');
        return (ReadFloat(parts[0]), ReadFloat(parts[1]), ReadFloat(parts[2]));
    }

    private static float ReadFloat(string s)
        => string.IsNullOrWhiteSpace(s) ? 0f : float.Parse(s.Trim(), CultureInfo.InvariantCulture);
}
