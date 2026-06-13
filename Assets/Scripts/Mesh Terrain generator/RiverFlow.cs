// ============================================================
//  RiverFlow.cs
//
//  Spawns a flat water plane mesh inside the river channel
//  of each active chunk and scrolls its UV to simulate flow.
//
//  HOW IT WORKS:
//   - Scans the chunk's terrain vertex data looking for river-
//     bank biome vertices (low elevation).
//   - Builds a thin sub-mesh at the lowest point of the channel.
//   - A scrolling UV material creates the flow illusion.
//
//  SETUP:
//   1. Add this component to the same GameObject as TerrainManager.
//   2. Assign a water material (e.g. URP Lit with a scrolling
//      normal map, or the SimpleWater shader from URP samples).
//   3. The river planes are created/destroyed with chunks
//      automatically.
//
//  NOTE: For a full physics river you'd need a particle or
//  compute-shader simulation. This gives a convincing visual
//  result at very low cost.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public class RiverFlow : MonoBehaviour
{
    [Header("Water")]
    public Material waterMaterial;

    [Tooltip("How fast the UV scrolls along the river (units/sec).")]
    public float flowSpeed = 0.3f;

    [Tooltip("Y offset above the terrain to prevent z-fighting.")]
    public float waterLift = 0.15f;

    // River channel height threshold (normalised 0..1).
    // Vertices below this are considered "in the river".
    [Range(0f, 0.3f)]
    public float channelHeightThreshold = 0.18f;

    // ── Internal ─────────────────────────────────────────────
    private Dictionary<Vector2Int, GameObject> _waterPlanes
        = new Dictionary<Vector2Int, GameObject>();

    private float _scrollOffset = 0f;

    // ─────────────────────────────────────────────────────────
    void Update()
    {
        _scrollOffset += flowSpeed * Time.deltaTime;

        // Update scroll on all active water materials
        if (waterMaterial != null)
            waterMaterial.SetTextureOffset("_BaseMap",
                new Vector2(0f, _scrollOffset % 1f));
    }

    // ─────────────────────────────────────────────────────────
    //  Called by TerrainChunk after mesh is applied
    // ─────────────────────────────────────────────────────────
    public void BuildRiverPlane(Vector2Int chunkCoord,
                                MeshData md,
                                int chunkSize,
                                int chunkWorldSize,
                                float heightMult)
    {
        if (waterMaterial == null) return;

        RemoveRiverPlane(chunkCoord);

        // Collect river-bed vertices
        var riverVerts = new List<Vector3>();
        float lowestY  = float.MaxValue;

        for (int i = 0; i < md.vertices.Length; i++)
        {
            float normH = md.vertices[i].y / heightMult;
            if (normH < channelHeightThreshold)
            {
                riverVerts.Add(md.vertices[i]);
                lowestY = Mathf.Min(lowestY, md.vertices[i].y);
            }
        }

        if (riverVerts.Count < 4) return;

        // Find bounding box of river points
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in riverVerts)
        {
            minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
            minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
        }

        // Build a simple quad plane for the water surface
        float waterY = lowestY + waterLift;
        var   go     = new GameObject("RiverWater_" + chunkCoord);
        go.transform.SetParent(transform);

        // World-space offset (chunk is already placed)
        go.transform.position = new Vector3(
            chunkCoord.x * chunkWorldSize, 0f,
            chunkCoord.y * chunkWorldSize);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = waterMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Create quad mesh
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(minX, waterY, minZ),
            new Vector3(maxX, waterY, minZ),
            new Vector3(maxX, waterY, maxZ),
            new Vector3(minX, waterY, maxZ),
        };
        mesh.triangles = new int[] { 0,2,1, 0,3,2 };
        mesh.uv = new Vector2[] {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1)
        };
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;

        _waterPlanes[chunkCoord] = go;
    }

    // ─────────────────────────────────────────────────────────
    public void RemoveRiverPlane(Vector2Int chunkCoord)
    {
        if (_waterPlanes.TryGetValue(chunkCoord, out var go))
        {
            if (go != null) Destroy(go);
            _waterPlanes.Remove(chunkCoord);
        }
    }
}
