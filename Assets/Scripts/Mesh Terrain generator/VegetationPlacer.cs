// ============================================================
//  VegetationPlacer.cs  (v2)
//
//  Reads VegetationPoint list from a completed MeshData and
//  spawns prefabs from pools – no Instantiate/Destroy spam.
//
//  SETUP:
//   1. Add this component to your TerrainManager GameObject.
//   2. Assign prefab arrays in the Inspector.
//   3. TerrainChunk calls PlaceVegetation() after mesh is applied.
//
//  Pooling strategy:
//   - Each category (tree, bush, rock, grass) has its own pool.
//   - Objects are deactivated/reactivated, never destroyed.
//   - Max pool size is capped to avoid memory growth.
//
//  Performance notes:
//   - All placements happen in a single frame after mesh apply,
//     but only a small fraction of vertices are candidates
//     (controlled by vegetationDensity in the generator).
//   - GrassRenderer uses GPU instancing via DrawMeshInstanced
//     so thousands of grass blades cost a single draw call.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public class VegetationPlacer : MonoBehaviour
{
    [Header("Tree Prefabs (at least 1 required for forest biome)")]
    public GameObject[] treePrefabs;

    [Header("Bush / Shrub Prefabs")]
    public GameObject[] bushPrefabs;

    [Header("Rock / Boulder Prefabs")]
    public GameObject[] rockPrefabs;

    [Header("Grass Mesh (used with GPU instancing)")]
    public Mesh grassMesh;
    public Material grassMaterial;

    [Header("Density Multipliers  (1 = default)")]
    [Range(0f, 3f)] public float treeDensity = 1f;
    [Range(0f, 3f)] public float bushDensity = 1f;
    [Range(0f, 3f)] public float rockDensity = 1f;
    [Range(0f, 3f)] public float grassDensity = 1f;

    [Header("Pool Sizes")]
    public int maxTreePool = 400;
    public int maxBushPool = 600;
    public int maxRockPool = 300;

    // ── Internal pools ───────────────────────────────────────
    private Queue<GameObject> treePool = new Queue<GameObject>();
    private Queue<GameObject> bushPool = new Queue<GameObject>();
    private Queue<GameObject> rockPool = new Queue<GameObject>();

    // Track what was placed per chunk so we can recycle it
    private Dictionary<Vector2Int, List<GameObject>> chunkObjects
        = new Dictionary<Vector2Int, List<GameObject>>();

    // GPU instanced grass matrices per chunk
    private Dictionary<Vector2Int, List<Matrix4x4>> chunkGrass
        = new Dictionary<Vector2Int, List<Matrix4x4>>();

    // ─────────────────────────────────────────────────────────
    //  Called by TerrainChunk after mesh is applied
    // ─────────────────────────────────────────────────────────
    public void PlaceVegetation(Vector2Int chunkCoord,
                                List<VegetationPoint> points,
                                float seed)
    {
        // Clear any previous objects on this chunk
        RecycleChunk(chunkCoord);

        var placed = new List<GameObject>();
        var grassMats = new List<Matrix4x4>();

        System.Random rng = new System.Random(
            (int)(chunkCoord.x * 73856093L ^ chunkCoord.y * 19349663L ^ (long)seed));

        foreach (var pt in points)
        {
            switch (pt.biome)
            {
                case BiomeType.Forest:
                    if (treePrefabs != null && treePrefabs.Length > 0
                        && rng.NextDouble() < 0.55 * treeDensity)
                        placed.Add(SpawnFromPool(treePool, treePrefabs, pt, rng, maxTreePool,
                                                 scaleMin: 0.7f, scaleMax: 1.6f));

                    if (bushPrefabs != null && bushPrefabs.Length > 0
                        && rng.NextDouble() < 0.30 * bushDensity)
                        placed.Add(SpawnFromPool(bushPool, bushPrefabs, pt, rng, maxBushPool,
                                                 scaleMin: 0.5f, scaleMax: 1.2f));

                    // Dense grass in forest
                    if (grassMesh && rng.NextDouble() < 0.7 * grassDensity)
                        grassMats.Add(GrassMatrix(pt, rng));
                    break;

                case BiomeType.PlainGrass:
                    if (treePrefabs != null && treePrefabs.Length > 0
                        && rng.NextDouble() < 0.08 * treeDensity)   // sparse trees on plains
                        placed.Add(SpawnFromPool(treePool, treePrefabs, pt, rng, maxTreePool,
                                                 scaleMin: 0.6f, scaleMax: 1.3f));

                    if (rockPrefabs != null && rockPrefabs.Length > 0
                        && rng.NextDouble() < 0.05 * rockDensity)
                        placed.Add(SpawnFromPool(rockPool, rockPrefabs, pt, rng, maxRockPool,
                                                 scaleMin: 0.3f, scaleMax: 1.0f));

                    if (grassMesh && rng.NextDouble() < 0.85 * grassDensity)
                        grassMats.Add(GrassMatrix(pt, rng));
                    break;

                case BiomeType.Valley:
                    if (treePrefabs != null && treePrefabs.Length > 0
                        && rng.NextDouble() < 0.25 * treeDensity)
                        placed.Add(SpawnFromPool(treePool, treePrefabs, pt, rng, maxTreePool,
                                                 scaleMin: 0.5f, scaleMax: 1.1f));

                    if (grassMesh && rng.NextDouble() < 0.60 * grassDensity)
                        grassMats.Add(GrassMatrix(pt, rng));
                    break;

                case BiomeType.RiverBank:
                    // Willow-like bushes near water
                    if (bushPrefabs != null && bushPrefabs.Length > 0
                        && rng.NextDouble() < 0.20 * bushDensity)
                        placed.Add(SpawnFromPool(bushPool, bushPrefabs, pt, rng, maxBushPool,
                                                 scaleMin: 0.6f, scaleMax: 1.4f));

                    if (grassMesh && rng.NextDouble() < 0.50 * grassDensity)
                        grassMats.Add(GrassMatrix(pt, rng));
                    break;

                // Mountain / Cliff: only rocks
                default:
                    if (rockPrefabs != null && rockPrefabs.Length > 0
                        && rng.NextDouble() < 0.12 * rockDensity)
                        placed.Add(SpawnFromPool(rockPool, rockPrefabs, pt, rng, maxRockPool,
                                                 scaleMin: 0.4f, scaleMax: 2.0f));
                    break;
            }
        }

        // Remove nulls (pool returned null when at capacity)
        placed.RemoveAll(g => g == null);

        chunkObjects[chunkCoord] = placed;
        chunkGrass[chunkCoord] = grassMats;
    }

    // ─────────────────────────────────────────────────────────
    //  Called by TerrainManager when a chunk is recycled
    // ─────────────────────────────────────────────────────────
    public void RecycleChunk(Vector2Int chunkCoord)
    {
        if (chunkObjects.TryGetValue(chunkCoord, out var objs))
        {
            foreach (var go in objs)
            {
                if (go == null) continue;
                go.SetActive(false);

                // Return to correct pool based on name prefix
                if (go.name.StartsWith("Tree")) treePool.Enqueue(go);
                else if (go.name.StartsWith("Bush")) bushPool.Enqueue(go);
                else rockPool.Enqueue(go);
            }
            chunkObjects.Remove(chunkCoord);
        }
        chunkGrass.Remove(chunkCoord);
    }

    // ─────────────────────────────────────────────────────────
    //  GPU-instanced grass rendered every frame
    // ─────────────────────────────────────────────────────────
    void Update()
    {
        if (grassMesh == null || grassMaterial == null) return;

        foreach (var kv in chunkGrass)
        {
            var mats = kv.Value;
            if (mats == null || mats.Count == 0) continue;

            // DrawMeshInstanced limit is 1023 per call
            int i = 0;
            while (i < mats.Count)
            {
                int count = Mathf.Min(1023, mats.Count - i);
                var batch = mats.GetRange(i, count);
                Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, batch);
                i += count;
            }
        }
    }

    // ── Pool helpers ─────────────────────────────────────────
    GameObject SpawnFromPool(Queue<GameObject> pool, GameObject[] prefabs,
                             VegetationPoint pt, System.Random rng,
                             int maxPool,
                             float scaleMin, float scaleMax)
    {
        // Don't exceed pool cap (prevents runaway memory)
        if (pool.Count + 1 > maxPool) return null;

        GameObject go;
        if (pool.Count > 0 && pool.Peek() != null)
        {
            go = pool.Dequeue();
            go.SetActive(true);
        }
        else
        {
            var prefab = prefabs[rng.Next(prefabs.Length)];
            if (prefab == null) return null;
            go = Instantiate(prefab, transform);
        }

        float yRot = (float)(rng.NextDouble() * 360.0);
        float scale = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());

        go.transform.position = pt.worldPos;
        go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
        go.transform.localScale = Vector3.one * scale;

        return go;
    }

    Matrix4x4 GrassMatrix(VegetationPoint pt, System.Random rng)
    {
        float yRot = (float)(rng.NextDouble() * 360.0);
        float scaleX = Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble());
        float scaleY = Mathf.Lerp(0.5f, 1.4f, (float)rng.NextDouble());
        var pos = pt.worldPos + Vector3.up * 0.01f;   // slightly above ground
        return Matrix4x4.TRS(pos,
                             Quaternion.Euler(0f, yRot, 0f),
                             new Vector3(scaleX, scaleY, scaleX));
    }
}