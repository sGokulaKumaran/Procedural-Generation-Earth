using UnityEngine;

public class MeshData
{
    public Vector3[] vertices;
    public Vector3[] normals;  // seam-correct normals computed by ChunkTerrainGenerator
    public int[] triangles;
    public Vector2[] uvs;        // main UV channel
    public Vector2[] uv2;        // side channel 1: x=dirt, y=cliff
    public Vector2[] uv3;        // side channel 2: x=variant id
    public Color[] colors;     // R=rock, G=snow, B=forest, A=grass
    public float[] curvature;  // 0=flat, 1=vertical
    public int VerticesPerSide { get; }

    public MeshData(int verticesPerSide)
    {
        VerticesPerSide = verticesPerSide;
        int vCount = verticesPerSide * verticesPerSide;
        int tCount = (verticesPerSide - 1) * (verticesPerSide - 1) * 6;

        vertices = new Vector3[vCount];
        normals = new Vector3[vCount];
        triangles = new int[tCount];
        uvs = new Vector2[vCount];
        uv2 = new Vector2[vCount];
        uv3 = new Vector2[vCount];
        colors = new Color[vCount];
        curvature = new float[vCount];
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.uv2 = uv2;
        mesh.uv3 = uv3;
        mesh.colors = colors;

        // Normals are supplied directly (computed from a padded world-space gradient in
        // ChunkTerrainGenerator) instead of calling RecalculateNormals(), which only sees
        // triangles inside this one chunk and produces visible lighting seams at borders.
        mesh.normals = normals;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }
}