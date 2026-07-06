// =============================================================================
// ChunkManager.cs
// (Same file as before, with the additions marked  // ★ LIVE EDIT
//  so you can see what changed.)
// =============================================================================

using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Chunks within this many rings of the player get a collider.")]
    [Range(0, 4)] public int colliderRadius = 2;

    [Tooltip("Resolution of the collider mesh, INDEPENDENT of the visual mesh tier.")]
    [Range(16, 128)] public int colliderVerts = 64;

    [Header("Finite World")]
    public bool finiteWorld = true;
    public int worldRadiusInChunks = 40;

    [Header("Cache")]
    public int heightmapCacheSize = 0;

    [Header("Noise / Shape")]
    public int seed = 42;
    public float baseFrequency = 0.0018f;
    [Range(1, 10)] public int octaves = 4;
    [Range(0.1f, 0.9f)] public float persistence = 0.48f;
    [Range(1.2f, 3.5f)] public float lacunarity = 2.1f;
    [Range(50f, 500f)] public float heightMult = 240f;
    [Range(0.5f, 4f)] public float exponent = 1.8f;
    [Range(0f, 1f)] public float mountainBlend = 0.85f;
    public float mountainFreq = 0.0022f;

    [Header("Regions (mountains / plains / valleys)")]
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
    [Range(0, 4)] public int unloadBufferChunks = 2;
    public bool usePredictiveLoading = true;
    [Range(0f, 5f)] public float predictiveBias = 2f;
    public bool autoConfigureFog = true;

    [Header("Performance")]
    [Range(1, 8)] public int maxConcurrentGenerations = 3;
    [Range(1, 8)] public int maxChunkSpawnsPerFrame = 4;
    [Range(1, 12)] public int maxMeshAppliesPerFrame = 3;
    [Range(1, 6)] public int maxColliderOpsPerFrame = 2;

    [Header("Initial Spawn")]
    [Range(1, 30)] public int initialChunksPerFrame = 3;

    // ★ LIVE EDIT ─────────────────────────────────────────────────────────────
    [Header("Live Edit")]
    [Tooltip("Watch the inspector fields above and rebuild the visible region " +
             "whenever any of them change. Tweak terrain values while playing.")]
    public bool liveEdit = true;

    [Tooltip("Throttle: how often (in seconds) to re-request active chunks. " +
             "Lower = snappier feedback, higher = less background churn.")]
    [Range(0.05f, 1f)] public float liveEditApplyInterval = 0.15f;
    // ★ LIVE EDIT ─────────────────────────────────────────────────────────────

    [Header("Debug")]
    public bool verboseLogging = false;

    // ── internal state ──
    float offsetX, offsetZ;
    readonly Dictionary<Vector2Int, Chunk> active = new Dictionary<Vector2Int, Chunk>();
    readonly Stack<Chunk> pool = new Stack<Chunk>();
    readonly ChunkDataCache dataCache = new ChunkDataCache();
    readonly ColliderMeshCache colliderCache = new ColliderMeshCache();
    ChunkGenerationScheduler scheduler;

    readonly object inFlightGate = new object();
    readonly HashSet<Vector2Int> inFlight = new HashSet<Vector2Int>();

    readonly ConcurrentQueue<ChunkApplyRequest> applyQueue = new ConcurrentQueue<ChunkApplyRequest>();
    readonly List<PendingSpawn> pending = new List<PendingSpawn>(256);
    readonly List<PendingSpawn> initialPending = new List<PendingSpawn>(64);
    readonly List<Vector2Int> colliderToBuildBuffer = new List<Vector2Int>(64);
    readonly List<KeyValuePair<Vector2Int, Chunk>> activeRescan = new List<KeyValuePair<Vector2Int, Chunk>>(64);

    bool initialSpawnDone = false;
    int initialSpawnIndex = 0;
    Vector3 previousPlayerPos;
    Vector2 smoothedMoveDir;

    // ★ LIVE EDIT ─────────────────────────────────────────────────────────────
    // Snapshot of the live-edited parameter values, taken at Start(). When the
    // current values diverge from this snapshot, we know the user (or a test
    // script) tweaked something in the inspector and we should rebuild the
    // world. Cheaper and more reliable than per-field dirty flags.
    struct LiveSnapshot
    {
        public int seed;
        public float baseFrequency;
        public int octaves;
        public float persistence;
        public float lacunarity;
        public float heightMult;
        public float exponent;
        public float mountainBlend;
        public float mountainFreq;
        public float regionFrequencyMultiplier;
        public float mountainRegionThreshold;
        public float mountainRegionSharpness;
        public float valleyRegionThreshold;
        public float valleyRegionSharpness;
        public float valleyDepth;
        public float continentInfluence;
    }
    LiveSnapshot lastSnapshot;
    bool snapshotTaken;
    float nextLiveEditTime;
    // ★ LIVE EDIT ─────────────────────────────────────────────────────────────

    struct PendingSpawn
    {
        public Vector2Int coord;
        public int distance;
        public int tier;
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
            chunkPrefab.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

        if (textureManager != null && terrainMaterial != null)
        {
            textureManager.terrainMaterial = terrainMaterial;
            textureManager.ConfigureMaterial();
        }

        dataCache.capacity = heightmapCacheSize > 0 ? heightmapCacheSize : ComputeRecommendedCacheCapacity();
        colliderCache.capacity = ComputeRecommendedColliderCacheCapacity();
        scheduler = new ChunkGenerationScheduler(maxConcurrentGenerations);
        previousPlayerPos = player.position;

        if (autoConfigureFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = Mathf.Max(0f, (farRadius - 1)) * chunkSize * 0.6f;
            RenderSettings.fogEndDistance = farRadius * chunkSize * 0.98f;
        }

        // ★ LIVE EDIT: take the initial snapshot so we can detect subsequent
        //              inspector edits without false positives from Start().
        lastSnapshot = CaptureSnapshot();
        snapshotTaken = true;
        nextLiveEditTime = 0f;

        if (verboseLogging)
            Debug.Log($"[ChunkManager] Initialised. Player at {player.position}, " +
                      $"chunkSize={chunkSize}, cacheCapacity={dataCache.capacity}, " +
                      $"colliderCacheCapacity={colliderCache.capacity}, " +
                      $"heightMult={heightMult}, exponent={exponent}");
    }

    int ComputeRecommendedCacheCapacity()
    {
        int r = farRadius + 2;
        return (2 * r + 1) * (2 * r + 1);
    }

    int ComputeRecommendedColliderCacheCapacity()
    {
        int r = colliderRadius + 1;
        return (2 * r + 1) * (2 * r + 1);
    }

    // ★ LIVE EDIT ─────────────────────────────────────────────────────────────
    LiveSnapshot CaptureSnapshot()
    {
        return new LiveSnapshot
        {
            seed = seed,
            baseFrequency = baseFrequency,
            octaves = octaves,
            persistence = persistence,
            lacunarity = lacunarity,
            heightMult = heightMult,
            exponent = exponent,
            mountainBlend = mountainBlend,
            mountainFreq = mountainFreq,
            regionFrequencyMultiplier = regionFrequencyMultiplier,
            mountainRegionThreshold = mountainRegionThreshold,
            mountainRegionSharpness = mountainRegionSharpness,
            valleyRegionThreshold = valleyRegionThreshold,
            valleyRegionSharpness = valleyRegionSharpness,
            valleyDepth = valleyDepth,
            continentInfluence = continentInfluence,
        };
    }

    bool SnapshotChanged(LiveSnapshot a, LiveSnapshot b)
    {
        // Epsilon-tolerant float compare so tiny slider jitter doesn't
        // constantly trigger full rebuilds.
        const float eps = 0.0001f;
        if (a.seed != b.seed) return true;
        if (a.octaves != b.octaves) return true;
        if (Mathf.Abs(a.baseFrequency - b.baseFrequency) > eps) return true;
        if (Mathf.Abs(a.persistence - b.persistence) > eps) return true;
        if (Mathf.Abs(a.lacunarity - b.lacunarity) > eps) return true;
        if (Mathf.Abs(a.heightMult - b.heightMult) > eps) return true;
        if (Mathf.Abs(a.exponent - b.exponent) > eps) return true;
        if (Mathf.Abs(a.mountainBlend - b.mountainBlend) > eps) return true;
        if (Mathf.Abs(a.mountainFreq - b.mountainFreq) > eps) return true;
        if (Mathf.Abs(a.regionFrequencyMultiplier - b.regionFrequencyMultiplier) > eps) return true;
        if (Mathf.Abs(a.mountainRegionThreshold - b.mountainRegionThreshold) > eps) return true;
        if (Mathf.Abs(a.mountainRegionSharpness - b.mountainRegionSharpness) > eps) return true;
        if (Mathf.Abs(a.valleyRegionThreshold - b.valleyRegionThreshold) > eps) return true;
        if (Mathf.Abs(a.valleyRegionSharpness - b.valleyRegionSharpness) > eps) return true;
        if (Mathf.Abs(a.valleyDepth - b.valleyDepth) > eps) return true;
        if (Mathf.Abs(a.continentInfluence - b.continentInfluence) > eps) return true;
        return false;
    }

    /// <summary>
    /// Called when an inspector edit is detected. We can't just clear the
    /// cache and let streaming re-fill it — the player is in the middle of
    /// the area and we want IMMEDIATE visual feedback, not "walk out and
    /// back in to see the change". So:
    ///   1. Clear the heightmap cache (stale data).
    ///   2. Clear the collider-mesh cache (stale colliders).
    ///   3. Bump the generation token on every active chunk so any background
    ///      jobs in flight from BEFORE the edit are discarded on return.
    ///   4. Re-request a fresh mesh for every active chunk at its current
    ///      tier. Because the heightmap cache is empty, these go through the
    ///      background path with the NEW parameters.
    ///   5. Clear each chunk's current collider (we'll rebuild them in
    ///      SyncActiveColliders on the same frame).
    /// </summary>
    void ApplyLiveEdit()
    {
        if (verboseLogging)
            Debug.Log("[ChunkManager] Live edit detected — rebuilding visible region.");

        // Stop any in-flight background jobs from the OLD parameters.
        // Bumping the token on every chunk means when those jobs come back,
        // ApplyQueuedMeshes will see token-mismatch and discard the result.
        activeRescan.Clear();
        foreach (var kv in active)
        {
            if (kv.Value != null) kv.Value.generationToken++;
            activeRescan.Add(kv);
        }

        // Wipe stale cached data.
        dataCache.Clear();   // assumes your ChunkDataCache has a Clear(); if not, see note below
        colliderCache.Clear();
        lock (inFlightGate) inFlight.Clear();

        // Re-request each active chunk with the new parameters.
        // Use a frame's normal spawn budget to keep it cheap — the rest will
        // follow on subsequent frames, but the closest ring goes first.
        int budget = maxChunkSpawnsPerFrame;
        for (int i = 0; i < activeRescan.Count && i < budget; i++)
        {
            var kv = activeRescan[i];
            Vector2Int coord = kv.Key;
            Chunk chunk = kv.Value;
            if (chunk == null) continue;

            // Clear the stale visual + collider so the chunk is in a known
            // state. SyncActiveColliders will rebuild the collider when the
            // new mesh comes back.
            if (chunk.mc != null) chunk.ApplyCollider(null);
            if (chunk.mf != null && chunk.mf.sharedMesh != null)
            {
                if (Application.isPlaying) Destroy(chunk.mf.sharedMesh);
                else DestroyImmediate(chunk.mf.sharedMesh);
                chunk.mf.sharedMesh = null;
            }

            int distance = Chebyshev(coord, WorldToChunkCoord(player.position));
            int tier = (distance <= fullRadius) ? 0 : (distance <= midRadius ? 1 : 2);
            chunk.resolutionTier = tier;
            RequestChunkMesh(chunk, coord, tier, distance);
        }

        // The remainder of the active list will be re-requested on the next
        // frame by the normal streaming logic (they're already in `active`,
        // and their tier didn't change, so without this the normal loop
        // would skip them). To make sure ALL of them rebuild (not just the
        // first few), mark them as needing a rebuild by resetting their tier
        // temporarily to -1.
        for (int i = budget; i < activeRescan.Count; i++)
        {
            var kv = activeRescan[i];
            if (kv.Value != null) kv.Value.resolutionTier = -1;
        }

        // Update the offset the noise uses for the NEW seed. Live-editing the
        // seed means the world is fundamentally a different place; we want
        // the player to see the new world, not stay in the same chunk and
        // get new noise.
        if (lastSnapshot.seed != seed)
        {
            var rng = new System.Random(seed);
            offsetX = (float)(rng.NextDouble() * 200000.0 - 100000.0);
            offsetZ = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        }

        lastSnapshot = CaptureSnapshot();
    }
    // ★ LIVE EDIT ─────────────────────────────────────────────────────────────

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

        // ★ LIVE EDIT: detect inspector changes and rebuild.
        if (liveEdit && snapshotTaken && Time.time >= nextLiveEditTime)
        {
            LiveSnapshot now = CaptureSnapshot();
            if (SnapshotChanged(lastSnapshot, now))
            {
                ApplyLiveEdit();
            }
            nextLiveEditTime = Time.time + liveEditApplyInterval;
        }
        // ★ LIVE EDIT

        if (!initialSpawnDone)
        {
            DoInitialSpawnStep(playerChunk);
        }
        else
        {
            ComputeNeededChunks(playerChunk, smoothedMoveDir, pending);

            int spawned = 0;
            for (int i = 0; i < pending.Count && spawned < maxChunkSpawnsPerFrame; i++)
            {
                PendingSpawn p = pending[i];

                if (active.TryGetValue(p.coord, out Chunk existing))
                {
                    // ★ LIVE EDIT: resolutionTier == -1 means "needs rebuild from edit"
                    if (existing.resolutionTier == -1 ||
                        existing.resolutionTier != p.tier ||
                        existing.state == Chunk.State.Idle)
                    {
                        existing.resolutionTier = p.tier;
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
        }

        SyncActiveColliders(playerChunk);
        ApplyQueuedMeshes();
    }

    void DoInitialSpawnStep(Vector2Int playerChunk)
    {
        if (initialPending.Count == 0)
        {
            ComputeNeededChunks(playerChunk, Vector2.zero, initialPending);
            initialSpawnIndex = 0;
            if (verboseLogging)
                Debug.Log($"[ChunkManager] Initial spawn around {playerChunk}: " +
                          $"{initialPending.Count} chunks @ {initialChunksPerFrame}/frame");
        }

        int spawnedThisFrame = 0;
        while (initialSpawnIndex < initialPending.Count && spawnedThisFrame < initialChunksPerFrame)
        {
            PendingSpawn p = initialPending[initialSpawnIndex++];

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
            spawnedThisFrame++;
        }

        if (initialSpawnIndex >= initialPending.Count)
        {
            initialSpawnDone = true;
            initialPending.Clear();
        }
    }

    void ComputeNeededChunks(Vector2Int playerChunk, Vector2 moveDir, List<PendingSpawn> output)
    {
        output.Clear();
        int maxR = farRadius;

        for (int dz = -maxR; dz <= maxR; dz++)
        {
            for (int dx = -maxR; dx <= maxR; dx++)
            {
                Vector2Int c = new Vector2Int(playerChunk.x + dx, playerChunk.y + dz);
                int d = Chebyshev(c, playerChunk);
                if (d > farRadius) continue;

                if (finiteWorld && (Mathf.Abs(c.x) > worldRadiusInChunks ||
                                    Mathf.Abs(c.y) > worldRadiusInChunks))
                    continue;

                int tier;
                if (d <= fullRadius) tier = 0;
                else if (d <= midRadius) tier = 1;
                else tier = 2;

                output.Add(new PendingSpawn { coord = c, distance = d, tier = tier });
            }
        }

        if (usePredictiveLoading && moveDir.sqrMagnitude > 0.0001f)
        {
            output.Sort((a, b) =>
            {
                float pa = a.distance;
                float pb = b.distance;
                if (a.distance > 0)
                {
                    Vector2Int da = a.coord - playerChunk;
                    Vector2 toA = new Vector2(da.x, da.y);
                    float la = toA.magnitude;
                    if (la > 0.001f) { toA /= la; pa -= Vector2.Dot(toA, moveDir) * predictiveBias; }
                }
                if (b.distance > 0)
                {
                    Vector2Int db = b.coord - playerChunk;
                    Vector2 toB = new Vector2(db.x, db.y);
                    float lb = toB.magnitude;
                    if (lb > 0.001f) { toB /= lb; pb -= Vector2.Dot(toB, moveDir) * predictiveBias; }
                }
                return pa.CompareTo(pb);
            });
        }
        else
        {
            output.Sort((a, b) => a.distance.CompareTo(b.distance));
        }
    }

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
            if (!inFlight.Add(coord)) return;
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

        while (applied < maxMeshAppliesPerFrame && applyQueue.TryDequeue(out var req))
        {
            if (req.chunk == null) continue;
            if (req.chunk.coord != req.coord || req.chunk.generationToken != req.token) continue;
            if (!req.chunk.gameObject.activeInHierarchy) continue;

            Mesh builtMesh;
            try
            {
                builtMesh = req.chunk.ApplyVisualMesh(req.data);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ChunkManager] ApplyVisualMesh exception on {req.coord}: " +
                                $"{e.Message}\n{e.StackTrace}");
                continue;
            }
            if (builtMesh == null) continue;

            req.chunk.resolutionTier = req.tier;
            applied++;
            req.chunk.PlaySpawnReveal();

            if (verboseLogging)
                Debug.Log($"[ChunkManager] Applied mesh {req.coord} tier={req.tier}");
        }
    }

    void SyncActiveColliders(Vector2Int playerChunk)
    {
        colliderToBuildBuffer.Clear();

        foreach (var kv in active)
        {
            Vector2Int coord = kv.Key;
            Chunk chunk = kv.Value;
            if (chunk == null) continue;

            int d = Chebyshev(coord, playerChunk);
            bool shouldHave = d <= colliderRadius;

            if (chunk.hasCollider == shouldHave) continue;

            if (!shouldHave)
            {
                chunk.ApplyCollider(null);
                continue;
            }

            if (colliderCache.TryGet(coord, out Mesh cachedCollider))
            {
                chunk.ApplyCollider(cachedCollider);
            }
            else
            {
                colliderToBuildBuffer.Add(coord);
            }
        }

        int ops = 0;
        for (int i = 0; i < colliderToBuildBuffer.Count; i++)
        {
            if (ops >= maxColliderOpsPerFrame) break;
            Vector2Int coord = colliderToBuildBuffer[i];

            if (!dataCache.TryGet(coord, out var entry))
                continue;

            var colliderData = ChunkMeshBuilder.Build(
                colliderVerts, chunkSize, coord.x * chunkSize, coord.y * chunkSize,
                entry.height, entry.flatness, entry.normals, entry.resolution,
                null, heightMult, offsetX, offsetZ);

            Mesh colliderMesh = colliderData.CreateMesh();
            colliderMesh.UploadMeshData(true);
            colliderCache.Put(coord, colliderMesh);

            if (active.TryGetValue(coord, out Chunk c) && c != null &&
                Chebyshev(coord, playerChunk) <= colliderRadius)
            {
                c.ApplyCollider(colliderMesh);
            }
            ops++;
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
