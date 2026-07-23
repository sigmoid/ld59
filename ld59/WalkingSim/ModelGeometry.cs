using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ld59.WalkingSim;

// Reads world-space triangles back from a built MonoGame Model by CPU-reading its vertex and
// index buffers. Used by the in-game navmesh baker to gather collision geometry from Mesh3D
// entities without any offline OBJ export. Vertices are duplicated per triangle (the baker
// welds coincident verts).
//
// Triangles are emitted with an UPWARD winding: Recast marks a face walkable only if its normal
// points up (within the slope limit), so a flat surface authored with downward-facing winding
// (a mirrored/negative-scaled mesh, or just modelled that way in Blender) would otherwise be
// rejected as a ceiling and bake to an empty navmesh. Flipping downward-facing triangles to face
// up is exactly the "you can walk on top of a roughly-flat surface" semantic the walker wants;
// Recast's headroom filters discard any surface that ends up buried under another.
public static class ModelGeometry
{
    public static void GatherWorldTriangles(Model model, Matrix world,
        List<float> verts, List<int> tris)
    {
        if (model == null) return;

        foreach (var mesh in model.Meshes)
        {
            Matrix meshWorld = mesh.ParentBone.Transform * world;

            foreach (var part in mesh.MeshParts)
            {
                var decl   = part.VertexBuffer.VertexDeclaration;
                int stride = decl.VertexStride;

                int posOffset = -1;
                foreach (var el in decl.GetVertexElements())
                    if (el.VertexElementUsage == VertexElementUsage.Position) { posOffset = el.Offset; break; }
                if (posOffset < 0) continue;

                // Read the whole vertex + index buffers to CPU. Content-pipeline models are
                // created readable; a WriteOnly buffer would throw here (caught by the caller).
                var vbytes = new byte[part.VertexBuffer.VertexCount * stride];
                part.VertexBuffer.GetData(vbytes);

                int idxCount = part.IndexBuffer.IndexCount;
                int[] indices;
                if (part.IndexBuffer.IndexElementSize == IndexElementSize.SixteenBits)
                {
                    var s = new ushort[idxCount];
                    part.IndexBuffer.GetData(s);
                    indices = Array.ConvertAll(s, x => (int)x);
                }
                else
                {
                    indices = new int[idxCount];
                    part.IndexBuffer.GetData(indices);
                }

                for (int t = 0; t < part.PrimitiveCount; t++)
                {
                    Vector3 a = ReadWorldVertex(vbytes, stride, posOffset, meshWorld,
                        part.VertexOffset + indices[part.StartIndex + t * 3 + 0]);
                    Vector3 b = ReadWorldVertex(vbytes, stride, posOffset, meshWorld,
                        part.VertexOffset + indices[part.StartIndex + t * 3 + 1]);
                    Vector3 c = ReadWorldVertex(vbytes, stride, posOffset, meshWorld,
                        part.VertexOffset + indices[part.StartIndex + t * 3 + 2]);

                    // Emit with an upward-facing winding. If this triangle's normal points down,
                    // swap two verts so Recast sees it as a floor rather than a ceiling.
                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.Y < 0f) (b, c) = (c, b);

                    AddVertex(verts, tris, a);
                    AddVertex(verts, tris, b);
                    AddVertex(verts, tris, c);
                }
            }
        }
    }

    private static Vector3 ReadWorldVertex(byte[] vbytes, int stride, int posOffset, Matrix world, int vi)
    {
        int off = vi * stride + posOffset;
        var local = new Vector3(
            BitConverter.ToSingle(vbytes, off + 0),
            BitConverter.ToSingle(vbytes, off + 4),
            BitConverter.ToSingle(vbytes, off + 8));
        return Vector3.Transform(local, world);
    }

    private static void AddVertex(List<float> verts, List<int> tris, Vector3 v)
    {
        tris.Add(verts.Count / 3);
        verts.Add(v.X);
        verts.Add(v.Y);
        verts.Add(v.Z);
    }
}
