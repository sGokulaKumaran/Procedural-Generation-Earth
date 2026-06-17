using System.Collections.Generic;
using UnityEditor.TerrainTools;
using UnityEngine;

public class TerrainManager : MonoBehaviour
{
    [Header("World")]
    public Transform player;
    [Range(1, 8)] public int viewDistance = 3;
    public int chunkSize = 256;
    public int chunkWorldSize = 256;
    public GameObject chunkPrefab;
    public Material terrainMaterial;

    [Header("Seed")]
    public int seed = 42;

    [Header("Terrain Shape")]
    public float baseFrequency = 0.0018f;
    [Range(1, 10)] public int octaves = 7;
    [Range(0.1f, 0.9f)] public float persistence = 0.48f;
    [Range(1.2f, 3.5f)] public float lacunarity = 2.1f;
    public float heightMult = 120f;
    [Range(0.5f, 4f)] public float exponent = 1.6f;
    [Header("Mountain")]
    public float mountainBlend = 0.55f;
    public float mountainFreq = 0.0022f;
    [Header("River")]
    public float riverFrequency = 0.00045f;
    [Range(0.01f, 0.15f)] public float riverWidth = 0.045f;
    public float riverDepth = 12f;
    [Range(0.01f, 0.20f)] public float bankWidth = 0.09f;
    [Range(0f, 1f)] public float bankFlatness = 0.60f;
    [Header("Valley")]
    public float valleyFrequency = 0.0008f;
    [Range(0f, 1f)] public float valleyDepth = 0.35f;

    private float offsetX, offsetZ;
    private Dictionary<Vector2Int, TerrainChunk> activeChunks = new Dictionary<Vector2Int, TerrainChunk>();
    private Queue<TerrainChunk> chunkPool = new Queue<TerrainChunk>();
    private Vector2Int lastPlayerChunk = new Vector2Int(int.MaxValue, int.MaxValue);

    void Start()
    {
        var rng = new System.Random(seed);
        offsetX = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        offsetZ = (float)(rng.NextDouble() * 200000.0 - 100000.0);

        if (player == null && Camera.main != null) player = Camera.main.transform;
    }

    void Update()
    {
        if (player == null) return;
        Vector2Int playerChunk = WorldToChunkCoord(player.position);
        if (playerChunk != lastPlayerChunk)
        {
            lastPlayerChunk = playerChunk;
            UpdateVisibleChunks(playerChunk);
        }
    }

    Vector2Int WorldToChunkCoord(Vector3 pos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(pos.x / chunkWorldSize),
            Mathf.FloorToInt(pos.z / chunkWorldSize)
        );
    }

    void UpdateVisibleChunks(Vector2Int center)
    {
        HashSet<Vector2Int> needed = new HashSet<Vector2Int>();

        for (int dx = -viewDistance; dx <= viewDistance; dx++)
            for (int dz = -viewDistance; dz <= viewDistance; dz++)
            {
                var coord = new Vector2Int(center.x + dx, center.y + dz);
                needed.Add(coord);

                if (!activeChunks.ContainsKey(coord))
                    SpawnChunk(coord);
            }

        // Assign neighbors to every active chunk
        foreach (var kv in activeChunks)
        {
            Vector2Int c = kv.Key;
            TerrainChunk chunk = kv.Value;
            chunk.neighborXPos = GetChunk(new Vector2Int(c.x + 1, c.y));
            chunk.neighborXNeg = GetChunk(new Vector2Int(c.x - 1, c.y));
            chunk.neighborZPos = GetChunk(new Vector2Int(c.x, c.y + 1));
            chunk.neighborZNeg = GetChunk(new Vector2Int(c.x, c.y - 1));
        }

        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kv in activeChunks)
        {
            if (!needed.Contains(kv.Key))
            {
                RecycleChunk(kv.Value);
                toRemove.Add(kv.Key);
            }
        }
        foreach (var k in toRemove) activeChunks.Remove(k);
        Debug.Log("Active Chunks = " + activeChunks.Count);
    }

    TerrainChunk GetChunk(Vector2Int coord)
    {
        activeChunks.TryGetValue(coord, out TerrainChunk chunk);
        return chunk;
    }

    void SpawnChunk(Vector2Int coord)
    {
        TerrainChunk chunk = GetOrCreateChunk();

        chunk.transform.position = new Vector3(
            coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);
        chunk.gameObject.SetActive(true);

        chunk.chunkWorldSize = chunkWorldSize;
        chunk.position = new Vector2(coord.x, coord.y);
        chunk.chunkSize = chunkSize;
        chunk.material = terrainMaterial;
        chunk.baseFrequency = baseFrequency;
        chunk.octaves = octaves;
        chunk.persistence = persistence;
        chunk.lacunarity = lacunarity;
        chunk.heightMult = heightMult;
        chunk.exponent = exponent;
        chunk.mountainBlend = mountainBlend;
        chunk.mountainFreq = mountainFreq;
        chunk.riverFrequency = riverFrequency;
        chunk.riverWidth = riverWidth;
        chunk.riverDepth = riverDepth;
        chunk.bankWidth = bankWidth;
        chunk.bankFlatness = bankFlatness;
        chunk.valleyFrequency = valleyFrequency;
        chunk.valleyDepth = valleyDepth;
        chunk.offsetX = offsetX;
        chunk.offsetZ = offsetZ;

        // Add to dictionary FIRST so the neighbor assignment loop can find it
        activeChunks.Add(coord, chunk);

        // Now assign neighbors using the dictionary
        chunk.neighborXPos = GetChunk(new Vector2Int(coord.x + 1, coord.y));
        chunk.neighborXNeg = GetChunk(new Vector2Int(coord.x - 1, coord.y));
        chunk.neighborZPos = GetChunk(new Vector2Int(coord.x, coord.y + 1));
        chunk.neighborZNeg = GetChunk(new Vector2Int(coord.x, coord.y - 1));

        // Finally, start the thread now that everything is ready
        chunk.StartThreadedGeneration();
        Debug.Log("Spawning " + coord);
    }


    TerrainChunk GetOrCreateChunk()
    {
        if (chunkPool.Count > 0)
        {
            TerrainChunk recycled = chunkPool.Dequeue();
            if (recycled != null) return recycled;
        }
        GameObject go = Instantiate(chunkPrefab, Vector3.zero, Quaternion.identity, transform);
        TerrainChunk tc = go.GetComponent<TerrainChunk>();
        if (tc == null) tc = go.AddComponent<TerrainChunk>();
        return tc;
    }

    void RecycleChunk(TerrainChunk chunk)
    {
        if (chunk == null) return;
        chunk.gameObject.SetActive(false);
        chunkPool.Enqueue(chunk);
    }
}
