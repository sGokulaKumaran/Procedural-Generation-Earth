using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Streams terrain chunks around the player, No Man's Sky style: a fixed-size world that
/// the player can never "see the seams of" while moving through it, with generation and
/// removal happening far enough away (and disguised well enough) that it isn't noticeable.
///
/// ARCHITECTURE (why it's built this way):
///
///  - Every chunk's terrain data is generated exactly ONCE, at full resolution, sampled at
///    true world-space coordinates (ChunkTerrainGenerator). This is the fix for the old
///    "every chunk looks identical" bug, which was caused by sampling noise using local
///    per-chunk coordinates instead of world position.
///  - That full-resolution data is cached per coordinate (ChunkDataCache, thread-safe).
///  - Building a mesh for ANY LOD tier (near/mid/far) is just a bilinear resample of the
///    cached full-res data (ChunkMeshBuilder) — cheap array math, no noise evaluation, safe
///    to run instantly on the main thread. This means LOD tier changes as the player moves
///    are effectively free and never need a background thread, which is both a major
///    performance win and what makes tier swaps invisible/instant.
///  - A background thread is ONLY ever spun up the first time a coordinate is seen. A
///    bounded scheduler (ChunkGenerationScheduler) caps how many run concurrently instead of
///    creating a raw OS thread per chunk.
///  - Pooled chunk objects carry a generation token that's bumped whenever they're reused for
///    a different coordinate, so a late-arriving background result from a chunk's *previous*
///    life can never be misapplied to its new one.
///  - All main-thread mesh/collider application is funneled through one rate-limited queue
///    (ConcurrentQueue, safe for many producers/one consumer) so spawning, LOD changes, and
///    first-time generation all share the same per-frame budget and never spike a frame.
///  - Chunks fade in (rise out of the ground) and fade out (sink away) instead of hard
///    popping, with a small random stagger so a whole ring never moves in lockstep.
/// </summary>
[DefaultExecutionOrder(-50)]
public class ChunkManager : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Chunk Grid")]
    public float chunkSize = 512;
    public int fullVerts = 256;
    [Range(0, 3)] public int fullRadius = 1;
    [Range(0, 4)] public int midRadius = 2;
    [Range(0, 6)] public int farRadius = 3;

    [Header("Per-Tier Resolution")]
    public int midVerts = 128;
    public int farVerts = 64;

    [Header("Collider")]
    [Range(0, 4)] public int colliderRadius = 2;

    [Header("Finite World")]
    public bool finiteWorld = true;
    [Tooltip("Half-extent of the world, in chunks, measured from chunk (0,0). The world is always this same size regardless of how far the player has walked.")]
    public int worldRadiusInChunks = 40;

    [Header("Cache")]
    [Tooltip("0 = auto-size based on farRadius so the working set never thrashes.")]
    public int heightmapCacheSize = 0;

    [Header("Noise / Shape")]
    public int seed = 42;
    public float baseFrequency = 0.0018f;
    [Range(1, 10)] public int octaves = 4;
    [Range(0.1f, 0.9f)] public float persistence = 0.48f;
    [Range(1.2f, 3.5f)] public float lacunarity = 2.1f;
    public float heightMult = 120f;
    [Range(0.5f, 4f)] public float exponent = 1.6f;
    public float mountainBlend = 0.85f;
    public float mountainFreq = 0.0022f;

    [Header("Regions (mountains / plains / valleys)")]
    [Tooltip("Lower = larger, more sweeping mountain ranges and valleys.")]
    public float regionFrequencyMultiplier = 0.08f;
    [Range(0f, 1f)] public float mountainRegionThreshold = 0.55f;
    [Range(0f, 1f)] public float mountainRegionSharpness = 0.74f;
    [Range(0f, 1f)] public float valleyRegionThreshold = 0.32f;
    [Range(0f, 1f)] public float valleyRegionSharpness = 0.14f;
    [Range(0f, 0.5f)] public float valleyDepth = 0.16f;
    [Range(0f, 1f)] public float continentInfluence = 0.30f;

    [Header("Chunk Visual")]
    public GameObject chunkPrefab;
    public Material terrainMaterial;
    public TextureManager textureManager;

    [Header("Streaming Feel")]
    [Tooltip("Extra rings kept loaded past farRadius before unloading, to stop chunks flickering in/out at the boundary.")]
    [Range(0, 4)] public int unloadBufferChunks = 2;
    public bool usePredictiveLoading = true;
    [Tooltip("How strongly chunks ahead of the player's movement direction jump the queue.")]
    [Range(0f, 5f)] public float predictiveBias = 2f;
    public bool autoConfigureFog = true;

    [Header("Performance")]
    [Range(1, 8)] public int maxConcurrentGenerations = 3;
    [Range(1, 8)] public int maxChunkSpawnsPerFrame = 4;
    [Range(1, 12)] public int maxMeshAppliesPerFrame = 3;
    [Range(1, 6)] public int maxColliderBakesPerFrame = 2;

    [Header("Debug")]
    public bool verboseLogging = false;

    // ── internal state ──
    float offsetX, offsetZ;
    readonly Dictionary<Vector2Int, Chunk> active = new Dictionary<Vector2Int, Chunk>();
    readonly Stack<Chunk> pool = new Stack<Chunk>();
    readonly ChunkDataCache dataCache = new ChunkDataCache();
    ChunkGenerationScheduler scheduler;

    readonly object inFlightGate = new object();
    readonly HashSet<Vector2Int> inFlight = new HashSet<Vector2Int>();

    readonly ConcurrentQueue<ChunkApplyRequest> applyQueue = new ConcurrentQueue<ChunkApplyRequest>();
    readonly List<PendingCollider> pendingColliders = new List<PendingCollider>();
    readonly List<PendingSpawn> pending = new List<PendingSpawn>(256);

    bool initialSpawnDone = false;
    Vector3 previousPlayerPos;
    Vector2 smoothedMoveDir;

    struct PendingSpawn
    {
        public Vector2Int coord;
        public int distance;
        public int tier;
        public float priority;
    }

    class ChunkApplyRequest
    {
        public Vector2Int coord;
        public Chunk chunk;
        public MeshData data;
        public int tier;
        public int distance;
        public int token;
    }

    class PendingCollider
    {
        public Chunk chunk;
        public Mesh mesh;
        public int token;
        public Vector2Int coord;
    }

    void Start()
    {
        var rng = new System.Random(seed);
        offsetX = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        offsetZ = (float)(rng.NextDouble() * 200000.0 - 100000.0);

        if (player == null && Camera.main != null)
            player = Camera.main.transform;

        if (player == null)
        {
            Debug.LogError("[ChunkManager] No player assigned and no Camera.main found. Aborting.");
            enabled = false;
            return;
        }

        if (chunkPrefab == null)
        {
            Debug.LogError("[ChunkManager] chunkPrefab is null. Assign it in the Inspector.");
            enabled = false;
            return;
        }

        var prefabChunk = chunkPrefab.GetComponent<Chunk>();
        if (prefabChunk == null)
        {
            Debug.LogError("[ChunkManager] chunkPrefab does not have a Chunk component on its root.");
            enabled = false;
            return;
        }

        if (terrainMaterial != null)
        {
            chunkPrefab.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        }

        if (textureManager != null && terrainMaterial != null)
        {
            textureManager.terrainMaterial = terrainMaterial;
            textureManager.ConfigureMaterial();
        }

        dataCache.capacity = heightmapCacheSize > 0 ? heightmapCacheSize : ComputeRecommendedCacheCapacity();
        scheduler = new ChunkGenerationScheduler(maxConcurrentGenerations);
        previousPlayerPos = player.position;

        if (autoConfigureFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = Mathf.Max(0f, (farRadius - 1)) * chunkSize * 0.6f;
            RenderSettings.fogEndDistance = farRadius * chunkSize * 0.98f;
        }

        if (verboseLogging)
            Debug.Log($"[ChunkManager] Initialised. Player at {player.position}, chunkSize={chunkSize}, cacheCapacity={dataCache.capacity}");
    }

    int ComputeRecommendedCacheCapacity()
    {
        int r = farRadius + 2;
        return (2 * r + 1) * (2 * r + 1);
    }

    void Update()
    {
        if (player == null) return;

        Vector2Int playerChunk = WorldToChunkCoord(player.position);

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 rawVelocity = (player.position - previousPlayerPos) / dt;
        previousPlayerPos = player.position;
        Vector2 rawDir = new Vector2(rawVelocity.x, rawVelocity.z);
        if (rawDir.sqrMagnitude > 0.0001f) rawDir.Normalize();
        smoothedMoveDir = Vector2.Lerp(smoothedMoveDir, rawDir, 0.15f);

        if (!initialSpawnDone)
        {
            EnsureInitialChunks(playerChunk);
            initialSpawnDone = true;
        }

        ComputeNeededChunks(playerChunk, smoothedMoveDir, pending);

        int spawned = 0;
        for (int i = 0; i < pending.Count && spawned < maxChunkSpawnsPerFrame; i++)
        {
            PendingSpawn p = pending[i];

            if (active.TryGetValue(p.coord, out Chunk existing))
            {
                if (existing.resolutionTier != p.tier || existing.state == Chunk.State.Idle)
                {
                    RequestChunkMesh(existing, p.coord, p.tier, p.distance);
                    spawned++;
                }
            }
            else
            {
                Chunk chunk = GetOrCreateChunk();
                if (chunk == null) continue;

                Vector3 worldPos = new Vector3(p.coord.x * chunkSize, 0f, p.coord.y * chunkSize);
                chunk.BeginUse(p.coord, worldPos);
                chunk.verticesPerSide = VertsForTier(p.tier);
                chunk.worldSize = chunkSize;
                chunk.Hide();
                chunk.gameObject.SetActive(true);
                active[p.coord] = chunk;

                RequestChunkMesh(chunk, p.coord, p.tier, p.distance);
                spawned++;
            }
        }
        pending.Clear();

        UnloadDistantChunks(playerChunk, farRadius + unloadBufferChunks);

        ApplyQueuedMeshes();
    }

    void EnsureInitialChunks(Vector2Int playerChunk)
    {
        ComputeNeededChunks(playerChunk, Vector2.zero, pending);
        if (verboseLogging) Debug.Log($"[ChunkManager] Initial spawn around {playerChunk}: {pending.Count} chunks");

        foreach (var p in pending)
        {
            Chunk chunk = GetOrCreateChunk();
            if (chunk == null) break;

            Vector3 worldPos = new Vector3(p.coord.x * chunkSize, 0f, p.coord.y * chunkSize);
            chunk.BeginUse(p.coord, worldPos);
            chunk.verticesPerSide = VertsForTier(p.tier);
            chunk.worldSize = chunkSize;
            chunk.Hide();
            chunk.gameObject.SetActive(true);
            active[p.coord] = chunk;

            RequestChunkMesh(chunk, p.coord, p.tier, p.distance);
        }
        pending.Clear();
    }

    void ComputeNeededChunks(Vector2Int playerChunk, Vector2 moveDir, List<PendingSpawn> output)
    {
        output.Clear();
        int maxR = Mathf.Max(Mathf.Max(fullRadius, midRadius), farRadius);

        for (int dz = -maxR; dz <= maxR; dz++)
        {
            for (int dx = -maxR; dx <= maxR; dx++)
            {
                Vector2Int c = new Vector2Int(playerChunk.x + dx, playerChunk.y + dz);
                int d = Chebyshev(c, playerChunk);
                if (d > farRadius) continue;

                if (finiteWorld && (Mathf.Abs(c.x) > worldRadiusInChunks || Mathf.Abs(c.y) > worldRadiusInChunks))
                    continue;

                int tier;
                if (d <= fullRadius) tier = 0;
                else if (d <= midRadius) tier = 1;
                else tier = 2;

                float priority = d;
                if (usePredictiveLoading && d > 0 && moveDir.sqrMagnitude > 0.0001f)
                {
                    Vector2 toChunk = new Vector2(dx, dz).normalized;
                    priority -= Vector2.Dot(toChunk, moveDir) * predictiveBias;
                }

                output.Add(new PendingSpawn { coord = c, distance = d, tier = tier, priority = priority });
            }
        }

        output.Sort((a, b) => a.priority.CompareTo(b.priority));
    }

    /// <summary>
    /// Gets (or schedules generation of) the mesh for one chunk coordinate at the given tier.
    /// Cache hit -> instant main-thread resample. Cache miss -> queued on the bounded
    /// background scheduler; only ever happens once per coordinate for the whole session
    /// (or until evicted from the LRU cache).
    /// </summary>
    void RequestChunkMesh(Chunk chunk, Vector2Int coord, int tier, int distance)
    {
        int token = chunk.generationToken;
        int targetVerts = VertsForTier(tier);
        float originX = coord.x * chunkSize;
        float originZ = coord.y * chunkSize;

        if (dataCache.TryGet(coord, out var entry))
        {
            var built = ChunkMeshBuilder.Build(
                targetVerts, chunkSize, originX, originZ,
                entry.height, entry.flatness, entry.normals, entry.resolution,
                textureManager, heightMult, offsetX, offsetZ);

            applyQueue.Enqueue(new ChunkApplyRequest
            {
                chunk = chunk,
                coord = coord,
                tier = tier,
                distance = distance,
                token = token,
                data = built,
            });
            return;
        }

        lock (inFlightGate)
        {
            if (!inFlight.Add(coord)) return; // already generating this coordinate
        }

        chunk.state = Chunk.State.Generating;

        scheduler.Enqueue(() =>
        {
            try
            {
                var gen = new ChunkTerrainGenerator
                {
                    ChunkOriginX = originX,
                    ChunkOriginZ = originZ,
                    ChunkSize = chunkSize,
                    VerticesPerSide = fullVerts,
                    SeedOffsetX = offsetX,
                    SeedOffsetZ = offsetZ,
                    Octaves = octaves,
                    BaseFrequency = baseFrequency,
                    Persistence = persistence,
                    Lacunarity = lacunarity,
                    HeightMult = heightMult,
                    Exponent = exponent,
                    MountainFreq = mountainFreq,
                    MountainBlend = mountainBlend,
                    RegionFrequencyMultiplier = regionFrequencyMultiplier,
                    MountainRegionThreshold = mountainRegionThreshold,
                    MountainRegionSharpness = mountainRegionSharpness,
                    ValleyRegionThreshold = valleyRegionThreshold,
                    ValleyRegionSharpness = valleyRegionSharpness,
                    ValleyDepth = valleyDepth,
                    ContinentInfluence = continentInfluence,
                };
                gen.Generate();

                dataCache.Put(coord, fullVerts, gen.HeightMap, gen.FlatnessMap, gen.Normals);

                var built = ChunkMeshBuilder.Build(
                    targetVerts, chunkSize, originX, originZ,
                    gen.HeightMap, gen.FlatnessMap, gen.Normals, fullVerts,
                    textureManager, heightMult, offsetX, offsetZ);

                applyQueue.Enqueue(new ChunkApplyRequest
                {
                    chunk = chunk,
                    coord = coord,
                    tier = tier,
                    distance = distance,
                    token = token,
                    data = built,
                });
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ChunkManager] Generation failed for {coord}: {e}");
            }
            finally
            {
                lock (inFlightGate) inFlight.Remove(coord);
            }
        });
    }

    void ApplyQueuedMeshes()
    {
        int applied = 0;
        int colliderApplied = 0;

        while (applied < maxMeshAppliesPerFrame && applyQueue.TryDequeue(out var req))
        {
            if (req.chunk == null) continue;
            if (req.chunk.coord != req.coord || req.chunk.generationToken != req.token) continue; // stale result
            if (!req.chunk.gameObject.activeInHierarchy) continue;

            Mesh builtMesh;
            try
            {
                builtMesh = req.chunk.ApplyVisualMesh(req.data);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ChunkManager] ApplyVisualMesh exception on {req.coord}: {e.Message}\n{e.StackTrace}");
                continue;
            }
            if (builtMesh == null) continue;

            req.chunk.resolutionTier = req.tier;
            req.chunk.wantsCollider = req.distance <= colliderRadius;
            applied++;
            req.chunk.PlaySpawnReveal();

            if (verboseLogging)
                Debug.Log($"[ChunkManager] Applied mesh {req.coord} tier={req.tier}");

            if (req.chunk.wantsCollider)
            {
                if (colliderApplied < maxColliderBakesPerFrame)
                {
                    req.chunk.ApplyCollider(builtMesh);
                    colliderApplied++;
                }
                else
                {
                    pendingColliders.Add(new PendingCollider
                    {
                        chunk = req.chunk,
                        mesh = builtMesh,
                        token = req.token,
                        coord = req.coord,
                    });
                }
            }
            else
            {
                req.chunk.ApplyCollider(null);
            }
        }

        for (int i = pendingColliders.Count - 1; i >= 0 && colliderApplied < maxColliderBakesPerFrame; i--)
        {
            PendingCollider pc = pendingColliders[i];
            bool stillValid = pc.chunk != null && pc.chunk.coord == pc.coord &&
                               pc.chunk.generationToken == pc.token && pc.chunk.wantsCollider;

            if (stillValid)
            {
                pc.chunk.ApplyCollider(pc.mesh);
                colliderApplied++;
            }
            pendingColliders.RemoveAt(i);
        }
    }

    void UnloadDistantChunks(Vector2Int playerChunk, int maxKeep)
    {
        List<Vector2Int> toRemove = null;
        foreach (var kv in active)
        {
            int d = Chebyshev(kv.Key, playerChunk);
            if (d > maxKeep)
            {
                if (toRemove == null) toRemove = new List<Vector2Int>();
                toRemove.Add(kv.Key);
            }
        }
        if (toRemove == null) return;

        foreach (var c in toRemove)
        {
            Chunk chunk = active[c];
            active.Remove(c);
            if (chunk != null)
            {
                chunk.PlayDespawnAndPool(finishedChunk =>
                {
                    finishedChunk.Hide();
                    finishedChunk.gameObject.SetActive(false);
                    pool.Push(finishedChunk);
                });
            }
        }
    }

    Chunk GetOrCreateChunk()
    {
        while (pool.Count > 0)
        {
            Chunk c = pool.Pop();
            if (c != null) return c;
        }
        if (chunkPrefab == null)
        {
            Debug.LogError("[ChunkManager] GetOrCreateChunk: chunkPrefab is null.");
            return null;
        }
        GameObject go = Instantiate(chunkPrefab, transform);
        Chunk tc = go.GetComponent<Chunk>();
        if (tc == null)
        {
            Debug.LogError("[ChunkManager] Instantiated prefab has no Chunk component.");
            Destroy(go);
            return null;
        }
        return tc;
    }

    int VertsForTier(int tier)
    {
        if (tier == 0) return fullVerts;
        if (tier == 1) return midVerts;
        return farVerts;
    }

    Vector2Int WorldToChunkCoord(Vector3 p) => new Vector2Int(
        Mathf.FloorToInt(p.x / chunkSize),
        Mathf.FloorToInt(p.z / chunkSize));

    static int Chebyshev(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
}