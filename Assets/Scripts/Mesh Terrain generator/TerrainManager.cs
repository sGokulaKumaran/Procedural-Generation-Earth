using System.Collections.Generic;
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

    [Header("Throttled Loading")]
    public ChunkLoadScheduler scheduler;

    [Header("Live Update")]
    [Tooltip("Enable live terrain updates when parameters change")]
    public bool enableLiveUpdate = true;
    [Tooltip("Seconds between chunk regenerations")]
    [Range(0.05f, 2.0f)] public float liveUpdateInterval = 0.15f;

    private float offsetX, offsetZ;
    private Dictionary<Vector2Int, TerrainChunk> activeChunks = new Dictionary<Vector2Int, TerrainChunk>();
    private Queue<TerrainChunk> chunkPool = new Queue<TerrainChunk>();
    private Vector2Int lastPlayerChunk = new Vector2Int(int.MaxValue, int.MaxValue);

    // Public accessors for LOD system
    public float GetOffsetX() => offsetX;
    public float GetOffsetZ() => offsetZ;

    // ── Live update state ──
    private bool hasParameterSnapshot = false;
    private Queue<TerrainChunk> regenQueue = new Queue<TerrainChunk>();
    private float liveUpdateTimer = 0f;

    // Parameter snapshots
    private float s_baseFrequency, s_persistence, s_lacunarity, s_heightMult, s_exponent;
    private float s_mountainBlend, s_mountainFreq;
    private float s_riverFrequency, s_riverWidth, s_riverDepth, s_bankWidth, s_bankFlatness;
    private float s_valleyFrequency, s_valleyDepth;
    private int s_octaves;

    void Start()
    {
        var rng = new System.Random(seed);
        offsetX = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        offsetZ = (float)(rng.NextDouble() * 200000.0 - 100000.0);

        if (player == null && Camera.main != null) player = Camera.main.transform;

        if (scheduler == null) scheduler = GetComponent<ChunkLoadScheduler>();
        if (scheduler == null) scheduler = gameObject.AddComponent<ChunkLoadScheduler>();

        scheduler.OnSpawnReady = HandleSpawnReady;

        if (player != null)
            SpawnPlayerStartingChunk();

        TakeParameterSnapshot();
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

        UpdateColliderLOD(playerChunk);

        if (enableLiveUpdate)
        {
            CheckAndQueueLiveUpdate();
            ProcessLiveRegenQueue(playerChunk);
        }
    }

    // ───────────────────────────────────────────────────
    //  Live Update System
    // ───────────────────────────────────────────────────

    void TakeParameterSnapshot()
    {
        s_baseFrequency = baseFrequency;
        s_octaves = octaves;
        s_persistence = persistence;
        s_lacunarity = lacunarity;
        s_heightMult = heightMult;
        s_exponent = exponent;
        s_mountainBlend = mountainBlend;
        s_mountainFreq = mountainFreq;
        s_riverFrequency = riverFrequency;
        s_riverWidth = riverWidth;
        s_riverDepth = riverDepth;
        s_bankWidth = bankWidth;
        s_bankFlatness = bankFlatness;
        s_valleyFrequency = valleyFrequency;
        s_valleyDepth = valleyDepth;
        hasParameterSnapshot = true;
    }

    bool HasParameterChanged()
    {
        if (!Mathf.Approximately(baseFrequency, s_baseFrequency)) return true;
        if (octaves != s_octaves) return true;
        if (!Mathf.Approximately(persistence, s_persistence)) return true;
        if (!Mathf.Approximately(lacunarity, s_lacunarity)) return true;
        if (!Mathf.Approximately(heightMult, s_heightMult)) return true;
        if (!Mathf.Approximately(exponent, s_exponent)) return true;
        if (!Mathf.Approximately(mountainBlend, s_mountainBlend)) return true;
        if (!Mathf.Approximately(mountainFreq, s_mountainFreq)) return true;
        if (!Mathf.Approximately(riverFrequency, s_riverFrequency)) return true;
        if (!Mathf.Approximately(riverWidth, s_riverWidth)) return true;
        if (!Mathf.Approximately(riverDepth, s_riverDepth)) return true;
        if (!Mathf.Approximately(bankWidth, s_bankWidth)) return true;
        if (!Mathf.Approximately(bankFlatness, s_bankFlatness)) return true;
        if (!Mathf.Approximately(valleyFrequency, s_valleyFrequency)) return true;
        if (!Mathf.Approximately(valleyDepth, s_valleyDepth)) return true;
        return false;
    }

    void CheckAndQueueLiveUpdate()
    {
        if (!hasParameterSnapshot)
        {
            TakeParameterSnapshot();
            return;
        }

        if (HasParameterChanged())
        {
            QueueAllChunksForRegen();
            TakeParameterSnapshot();
        }
    }

    void QueueAllChunksForRegen()
    {
        regenQueue.Clear();

        foreach (var kv in activeChunks)
        {
            if (kv.Value != null && kv.Value.gameObject.activeInHierarchy)
                regenQueue.Enqueue(kv.Value);
        }
    }

    void ProcessLiveRegenQueue(Vector2Int playerChunk)
    {
        if (regenQueue.Count == 0) return;

        liveUpdateTimer += Time.deltaTime;
        if (liveUpdateTimer < liveUpdateInterval) return;
        liveUpdateTimer = 0f;

        // Sort by distance to player
        if (regenQueue.Count > 1)
        {
            List<TerrainChunk> tempList = new List<TerrainChunk>();
            while (regenQueue.Count > 0)
                tempList.Add(regenQueue.Dequeue());

            tempList.Sort((a, b) =>
            {
                Vector2Int coordA = GetChunkCoordFromTransform(a);
                Vector2Int coordB = GetChunkCoordFromTransform(b);
                int distA = Chebyshev(coordA, playerChunk);
                int distB = Chebyshev(coordB, playerChunk);
                return distA.CompareTo(distB);
            });

            foreach (var chunk in tempList)
                regenQueue.Enqueue(chunk);
        }

        TerrainChunk chunkToRegen = regenQueue.Dequeue();
        if (chunkToRegen == null || !chunkToRegen.gameObject.activeInHierarchy) return;

        RegenerateChunk(chunkToRegen);
    }

    Vector2Int GetChunkCoordFromTransform(TerrainChunk chunk)
    {
        int x = Mathf.FloorToInt(chunk.transform.position.x / chunkWorldSize);
        int z = Mathf.FloorToInt(chunk.transform.position.z / chunkWorldSize);
        return new Vector2Int(x, z);
    }

    void RegenerateChunk(TerrainChunk chunk)
    {
        Vector2Int coord = GetChunkCoordFromTransform(chunk);
        CopyGenParams(chunk);
        chunk.position = new Vector2(coord.x, coord.y);

        chunk.RevokeCollider();
        chunk.state = TerrainChunk.ChunkState.Idle;
        chunk.StartThreadedGeneration();
    }

    // ───────────────────────────────────────────────────
    //  Original Methods (unchanged)
    // ───────────────────────────────────────────────────

    void SpawnPlayerStartingChunk()
    {
        Vector2Int pc = WorldToChunkCoord(player.position);

        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int c = new Vector2Int(pc.x + dx, pc.y + dz);
                SpawnChunkImmediate(c, pc, forceCollider: true);
            }

        DriveImmediateLoading();
    }

    void SpawnChunkImmediate(Vector2Int coord, Vector2Int playerChunk, bool forceCollider)
    {
        if (activeChunks.ContainsKey(coord)) return;

        TerrainChunk chunk = GetOrCreateChunk();
        chunk.ResetForPool();
        chunk.transform.position = new Vector3(
            coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);
        chunk.gameObject.SetActive(true);

        CopyGenParams(chunk);
        chunk.needsCollider = forceCollider;
        chunk.position = new Vector2(coord.x, coord.y);

        activeChunks[coord] = chunk;
    }

    void CopyGenParams(TerrainChunk chunk)
    {
        chunk.chunkWorldSize = chunkWorldSize;
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
    }

    void DriveImmediateLoading()
    {
        foreach (var kv in activeChunks)
        {
            if (kv.Value.state == TerrainChunk.ChunkState.Idle)
                kv.Value.StartThreadedGeneration();
        }

        foreach (var kv in activeChunks)
        {
            kv.Value.needsCollider = true;
        }
    }

    Vector2Int WorldToChunkCoord(Vector3 pos) => new Vector2Int(
        Mathf.FloorToInt(pos.x / chunkWorldSize),
        Mathf.FloorToInt(pos.z / chunkWorldSize));

    void UpdateVisibleChunks(Vector2Int center)
    {
        HashSet<Vector2Int> needed = new HashSet<Vector2Int>();

        for (int dx = -viewDistance; dx <= viewDistance; dx++)
            for (int dz = -viewDistance; dz <= viewDistance; dz++)
            {
                Vector2Int coord = new Vector2Int(center.x + dx, center.y + dz);
                needed.Add(coord);

                if (!activeChunks.ContainsKey(coord))
                    RequestChunkSpawn(coord, center);
            }

        foreach (var kv in activeChunks)
        {
            Vector2Int c = kv.Key;
            kv.Value.neighborXPos = GetChunk(new Vector2Int(c.x + 1, c.y));
            kv.Value.neighborXNeg = GetChunk(new Vector2Int(c.x - 1, c.y));
            kv.Value.neighborZPos = GetChunk(new Vector2Int(c.x, c.y + 1));
            kv.Value.neighborZNeg = GetChunk(new Vector2Int(c.x, c.y - 1));
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
    }

    void RequestChunkSpawn(Vector2Int coord, Vector2Int playerChunk)
    {
        TerrainChunk chunk = GetOrCreateChunk();

        chunk.ResetForPool();
        chunk.transform.position = new Vector3(
            coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);
        chunk.gameObject.SetActive(true);

        CopyGenParams(chunk);

        int dist = Chebyshev(coord, playerChunk);
        chunk.needsCollider = dist <= scheduler.colliderDistance;
        chunk.position = new Vector2(coord.x, coord.y);

        activeChunks[coord] = chunk;

        chunk.neighborXPos = GetChunk(new Vector2Int(coord.x + 1, coord.y));
        chunk.neighborXNeg = GetChunk(new Vector2Int(coord.x - 1, coord.y));
        chunk.neighborZPos = GetChunk(new Vector2Int(coord.x, coord.y + 1));
        chunk.neighborZNeg = GetChunk(new Vector2Int(coord.x, coord.y - 1));

        scheduler.EnqueueSpawn(coord, chunk, dist);
    }

    void HandleSpawnReady(ChunkLoadScheduler.SpawnRequest req)
    {
        TerrainChunk chunk = req.chunk;
        if (chunk == null || !chunk.gameObject.activeInHierarchy) return;
        chunk.StartThreadedGeneration();
    }

    void UpdateColliderLOD(Vector2Int playerChunk)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int c = new Vector2Int(playerChunk.x + dx, playerChunk.y + dz);
                if (!activeChunks.TryGetValue(c, out TerrainChunk center)) continue;

                if (center.NeedsColliderMesh() && !center.needsCollider)
                {
                    center.needsCollider = true;
                    scheduler.EnqueueColliderAdd(center, 0);
                }
            }

        foreach (var kv in activeChunks)
        {
            TerrainChunk chunk = kv.Value;
            if (chunk == null) continue;

            int dist = Chebyshev(kv.Key, playerChunk);
            if (dist <= 1) continue;

            bool needs = dist <= scheduler.colliderDistance;

            if (needs)
            {
                if (chunk.NeedsColliderMesh() && !chunk.needsCollider)
                {
                    chunk.needsCollider = true;
                    scheduler.EnqueueColliderAdd(chunk, dist);
                }
            }
            else
            {
                if (chunk.needsCollider)
                {
                    if (kv.Key == playerChunk) continue;
                    scheduler.EnqueueColliderRevoke(chunk);
                    chunk.needsCollider = false;
                }
            }
        }
    }

    public TerrainChunk GetChunk(Vector2Int coord)
    {
        activeChunks.TryGetValue(coord, out TerrainChunk chunk);
        return chunk;
    }

    public TerrainChunk GetOrCreateChunk()
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

    static int Chebyshev(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
}
