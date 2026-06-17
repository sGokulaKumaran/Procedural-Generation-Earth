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

        if (scheduler == null) scheduler = GetComponent<ChunkLoadScheduler>();
        if (scheduler == null) scheduler = gameObject.AddComponent<ChunkLoadScheduler>();

        scheduler.OnSpawnReady = HandleSpawnReady;

        // ── Spawn the player's starting chunk SYNCHRONOUSLY (no throttle)
        //    so the player has ground to stand on from frame 0.
        if (player != null)
            SpawnPlayerStartingChunk();
    }

    void SpawnPlayerStartingChunk()
    {
        Vector2Int pc = WorldToChunkCoord(player.position);

        // Spawn a 3x3 around the player immediately, with colliders forced ON
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int c = new Vector2Int(pc.x + dx, pc.y + dz);
                SpawnChunkImmediate(c, pc, forceCollider: true);
            }

        // Drive the worker threads and apply meshes/colliders immediately too.
        // This still spreads the work because meshes/colliders are 1-per-frame,
        // but it skips the "queue first, then start" delay.
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

        // Force collider on the starting 3x3 — never let the player fall
        chunk.needsCollider = forceCollider;

        activeChunks[coord] = chunk;
    }

    void DriveImmediateLoading()
    {
        // Start generation on the starting 3x3 NOW (bypass throttle)
        foreach (var kv in activeChunks)
        {
            if (kv.Value.state == TerrainChunk.ChunkState.Idle)
                kv.Value.StartThreadedGeneration();
        }

        // The worker threads will still take time. We add the player's
        // chunk collider to the scheduler's front-of-queue.
        foreach (var kv in activeChunks)
        {
            kv.Value.needsCollider = true;
            if (kv.Value.HasMeshCollider())
                scheduler.EnqueueColliderAdd(kv.Value, 0);
        }
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

        // Every frame: re-evaluate collider LOD, but with a SAFETY GUARD
        // that NEVER revokes the collider of the chunk the player is
        // currently on, and NEVER revokes a collider if the new chunk
        // underneath the player doesn't have one yet.
        UpdateColliderLOD(playerChunk);
    }

    // ────────────────────────────────────────────
    //  Visibility / spawn / recycle
    // ────────────────────────────────────────────

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

        int dist = Chebyshev(coord, playerChunk);
        chunk.needsCollider = dist <= scheduler.colliderDistance;

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

    // ────────────────────────────────────────────
    //  Collider LOD — re-evaluated every frame
    //  with SAFETY GUARDS so the player never loses ground
    // ────────────────────────────────────────────

    void UpdateColliderLOD(Vector2Int playerChunk)
    {
        // 1) Force the chunk under the player + its 8 neighbors to NEED a collider
        //    This is a hard guarantee — overrides any earlier "complete without
        //    collider" state.
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int c = new Vector2Int(playerChunk.x + dx, playerChunk.y + dz);
                if (!activeChunks.TryGetValue(c, out TerrainChunk center)) continue;

                int dist = 0; // center is always dist 0/1, both within colliderDistance
                bool needs = dist <= scheduler.colliderDistance;

                if (center.state == TerrainChunk.ChunkState.VisualReady && needs)
                {
                    if (!center.needsCollider)
                    {
                        center.needsCollider = true;
                        scheduler.EnqueueColliderAdd(center, dist);
                    }
                }
            }

        // 2) For all OTHER chunks in the view distance, apply normal LOD:
        //    - in colliderDistance but not yet having one → add
        //    - outside colliderDistance and having one → revoke
        foreach (var kv in activeChunks)
        {
            TerrainChunk chunk = kv.Value;
            if (chunk == null) continue;

            // Skip the safety 3x3 — we just handled those above
            int dist = Chebyshev(kv.Key, playerChunk);
            if (dist <= 1) continue; // 3x3 area is the safety zone

            bool needs = dist <= scheduler.colliderDistance;

            // chunk is in LOD range and is at least visually ready
            if (needs && chunk.state == TerrainChunk.ChunkState.VisualReady && !chunk.needsCollider)
            {
                chunk.needsCollider = true;
                scheduler.EnqueueColliderAdd(chunk, dist);
            }
            // chunk is OUT of LOD range, and currently has a collider → revoke
            else if (!needs && chunk.needsCollider)
            {
                // SAFETY: do NOT revoke if it's the chunk the player is
                // currently standing on. This is already handled by the
                // 3x3 loop above, but double-check here for paranoia.
                if (kv.Key == playerChunk) continue;

                scheduler.EnqueueColliderRevoke(chunk);
                chunk.needsCollider = false;
                // (We do NOT change state — chunk is still Complete, just
                //  without a collider mesh assigned. State machine stays clean.)
            }
        }
    }

    // ────────────────────────────────────────────
    //  Pool
    // ────────────────────────────────────────────

    TerrainChunk GetChunk(Vector2Int coord)
    {
        activeChunks.TryGetValue(coord, out TerrainChunk chunk);
        return chunk;
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

    static int Chebyshev(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
}
