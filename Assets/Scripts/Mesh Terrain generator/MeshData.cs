// ============================================================
//  MeshData.cs  (v2)
//  Raw geometry container.  32-bit index buffer required for
//  anything over 65 535 vertices (e.g. 257x257 = 66 049).
//
//  Extra fields vs v1:
//   - normals[]  stored so the background thread can pre-compute
//     smooth normals (saves the main-thread RecalculateNormals call)
//   - SpawnPoints list for vegetation (filled by generator thread)
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public struct VegetationPoint
{
    public Vector3   worldPos;
    public Vector3   normal;
    public BiomeType biome;
    public float     slope;    // 0 = flat, 1 = vertical
}

public class MeshData
{
    public Vector3[] vertices;
    public int[]     triangles;
    public Vector2[] uvs;
    public Color[]   colors;


    // Vegetation spawn candidates (populated on background thread)
    public List<VegetationPoint> spawnPoints = new List<VegetationPoint>();

    public MeshData(int vpl)
    {
        int vc = vpl * vpl;
        int tc = (vpl - 1) * (vpl - 1) * 6;
        vertices  = new Vector3[vc];
        triangles = new int[tc];
        uvs       = new Vector2[vc];
        colors    = new Color[vc];
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices    = vertices;
        mesh.triangles   = triangles;
        mesh.uv          = uvs;
        mesh.colors      = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();


        return mesh;
    }
}
