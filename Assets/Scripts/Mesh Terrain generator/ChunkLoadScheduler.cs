// ============================================================
//  ChunkLoadScheduler.cs
//  LOD-style throttled chunk loading.
//  Spreads expensive main-thread operations across multiple
//  frames so chunk generation does not cause FPS hitches.
//
//  Frame Budgets:
//   - maxSpawnsPerFrame          -> new chunk threads started
//   - maxMeshAppliesPerFrame     -> MeshFilter.mesh assignments
//   - maxColliderUpdatesPerFrame -> MeshCollider.sharedMesh (the big hitch)
//
//  LOD:
//   - colliderDistance           -> only chunks within N chunks of
//                                   the player get MeshColliders
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public class ChunkLoadScheduler : MonoBehaviour
{
    [Header("Frame Budgets")]
    [Tooltip("Max new chunk threads started per frame. Smooths CPU spikes.")]
    [Range(1, 10)] public int maxSpawnsPerFrame = 2;

    [Tooltip("Max MeshFilter.mesh assignments per frame.")]
    [Range(1, 6)] public int maxMeshAppliesPerFrame = 1;

    [Tooltip("Max MeshCollider updates per frame. THIS is the biggest hitch — keep small.")]
    [Range(0, 4)] public int maxColliderUpdatesPerFrame = 1;

    [Header("Timing")]
    [Tooltip("Minimum seconds between chunk spawns (smooths the reveal).")]
    [Range(0f, 0.5f)] public float minSpawnInterval = 0.04f;

    [Header("LOD")]
    [Tooltip("Chunks beyond this Chebyshev distance skip their MeshCollider.")]
    [Range(0, 6)] public int colliderDistance = 2;

    // Pending spawn requests (sorted by distance each frame)
    private readonly List<SpawnRequest> spawnList = new List<SpawnRequest>();

    // FIFO queues
    private readonly Queue<TerrainChunk> meshQueue = new Queue<TerrainChunk>();
    private readonly Queue<TerrainChunk> colliderQueue = new Queue<TerrainChunk>();

    // Per-frame counters
    private int spawnsThisFrame;
    private int meshesThisFrame;
    private int collidersThisFrame;
    private float lastSpawnTime;

    // Manager hooks this to actually call chunk.StartThreadedGeneration()
    public System.Action<SpawnRequest> OnSpawnReady;

    public struct SpawnRequest
    {
        public Vector2Int coord;
        public TerrainChunk chunk;
        public int distance;   // Chebyshev distance from player chunk
    }

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        // Reset per-frame budgets at the start of each frame
        spawnsThisFrame = 0;
        meshesThisFrame = 0;
        collidersThisFrame = 0;
    }

    void LateUpdate()
    {
        ProcessSpawnQueue();
        ProcessMeshQueue();
        ProcessColliderQueue();
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API (called by TerrainManager and TerrainChunk)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Manager requests a chunk to be spawned (throttled).</summary>
    public void EnqueueSpawn(Vector2Int coord, TerrainChunk chunk, int distance)
    {
        if (chunk == null) return;
        spawnList.Add(new SpawnRequest { coord = coord, chunk = chunk, distance = distance });
    }

    /// <summary>Chunk notifies the scheduler that its worker thread finished.</summary>
    public void EnqueueMeshApply(TerrainChunk chunk)
    {
        if (chunk != null) meshQueue.Enqueue(chunk);
    }

    /// <summary>
    /// Manager asks for the chunk's collider to be applied (throttled).
    /// If the chunk is too far away, it's marked complete with no collider.
    /// </summary>
    public void EnqueueColliderUpdate(TerrainChunk chunk, int distanceFromPlayer)
    {
        if (chunk == null) return;

        bool needs = distanceFromPlayer <= colliderDistance;
        chunk.needsCollider = needs;

        if (needs && chunk.HasMeshCollider())
            colliderQueue.Enqueue(chunk);
        else
            chunk.state = TerrainChunk.ChunkState.Complete; // skip collider
    }

    /// <summary>Flush all queues (e.g., on player teleport).</summary>
    public void Clear()
    {
        spawnList.Clear();
        meshQueue.Clear();
        colliderQueue.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal queue processors
    // ─────────────────────────────────────────────────────────────

    private void ProcessSpawnQueue()
    {
        if (spawnList.Count == 0) return;

        // Closest chunks spawn first — player sees near terrain first
        spawnList.Sort((a, b) => a.distance.CompareTo(b.distance));

        int i = 0;
        while (i < spawnList.Count)
        {
            if (spawnsThisFrame >= maxSpawnsPerFrame) break;
            if (Time.unscaledTime - lastSpawnTime < minSpawnInterval) break;

            SpawnRequest req = spawnList[i];
            spawnList.RemoveAt(i);

            // Chunk may have been recycled before its turn came up
            if (req.chunk == null || !req.chunk.gameObject.activeInHierarchy)
                continue;

            if (OnSpawnReady != null) OnSpawnReady.Invoke(req);

            spawnsThisFrame++;
            lastSpawnTime = Time.unscaledTime;
            // do not increment i; RemoveAt shifted next item down
        }
    }

    private void ProcessMeshQueue()
    {
        int safety = maxMeshAppliesPerFrame * 4 + 4;
        while (meshQueue.Count > 0 && meshesThisFrame < maxMeshAppliesPerFrame && safety-- > 0)
        {
            TerrainChunk chunk = meshQueue.Dequeue();
            if (chunk == null || !chunk.gameObject.activeInHierarchy) continue;
            if (chunk.state != TerrainChunk.ChunkState.MeshPending) continue;

            if (chunk.ApplyVisualMesh())
            {
                meshesThisFrame++;
                // Decide about collider based on the chunk's needsCollider flag
                if (chunk.needsCollider && chunk.HasMeshCollider())
                    colliderQueue.Enqueue(chunk);
                else
                    chunk.state = TerrainChunk.ChunkState.Complete;
            }
        }
    }

    private void ProcessColliderQueue()
    {
        int safety = maxColliderUpdatesPerFrame * 4 + 4;
        while (colliderQueue.Count > 0 &&
               collidersThisFrame < maxColliderUpdatesPerFrame && safety-- > 0)
        {
            TerrainChunk chunk = colliderQueue.Dequeue();
            if (chunk == null || !chunk.gameObject.activeInHierarchy) continue;
            if (chunk.state != TerrainChunk.ChunkState.VisualReady) continue;

            if (chunk.ApplyCollider())
                collidersThisFrame++;
        }
    }
}
