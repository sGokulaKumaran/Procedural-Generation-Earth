// ============================================================
//  MeshData.cs
//  Holds raw vertex/triangle/UV data and assembles a Unity Mesh.
//  32-bit index buffer lets us handle 1000x1000 (1M vertices).
// ============================================================
using UnityEngine;

public class MeshData
{
    public Vector3[] vertices;
    public int[]     triangles;
    public Vector2[] uvs;
    public Color[]   colors;   // vertex colours used for biome blending

    public MeshData(int verticesPerLine)
    {
        int vCount = verticesPerLine * verticesPerLine;
        int tCount = (verticesPerLine - 1) * (verticesPerLine - 1) * 6;

        vertices  = new Vector3[vCount];
        triangles = new int[tCount];
        uvs       = new Vector2[vCount];
        colors    = new Color[vCount];
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();

        // REQUIRED for >65535 vertices (1000x1000 = 1,000,000 verts)
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices  = vertices;
        mesh.triangles = triangles;
        mesh.uv        = uvs;
        mesh.colors    = colors;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
