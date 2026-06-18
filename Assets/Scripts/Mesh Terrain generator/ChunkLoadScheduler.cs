using System.Collections.Generic;
using UnityEngine;
using static TerrainChunk;

public class ChunkLoadScheduler : MonoBehaviour
{
    [Header("Frame Budgets")]
    [Range(1, 10)] public int maxSpawnsPerFrame = 2;
    [Range(1, 6)] public int maxMeshAppliesPerFrame = 1;
    [Range(0, 4)] public int maxColliderUpdatesPerFrame = 1;

    [Header("Timing")]
    [Range(0f, 0.5f)] public float minSpawnInterval = 0.04f;

    [Header("LOD")]
    [Range(0, 6)] public int colliderDistance = 2;

    private readonly List<SpawnRequest> spawnList = new List<SpawnRequest>();
    private readonly Queue<TerrainChunk> meshQueue = new Queue<TerrainChunk>();
    private readonly Queue<TerrainChunk> colliderQueue = new Queue<TerrainChunk>();
    private readonly Queue<TerrainChunk> colliderRevokeQueue = new Queue<TerrainChunk>();

    private int spawnsThisFrame, meshesThisFrame, collidersThisFrame, revokesThisFrame;
    private float lastSpawnTime;

    public System.Action<SpawnRequest> OnSpawnReady;

    public struct SpawnRequest
    {
        public Vector2Int coord;
        public TerrainChunk chunk;
        public int distance;
    }

    void Update()
    {
        spawnsThisFrame = 0;
        meshesThisFrame = 0;
        collidersThisFrame = 0;
        revokesThisFrame = 0;
    }

    void LateUpdate()
    {
        ProcessSpawnQueue();
        ProcessMeshQueue();
        ProcessColliderQueue();
        ProcessColliderRevokeQueue();
    }

    public void EnqueueSpawn(Vector2Int coord, TerrainChunk chunk, int distance)
    {
        if (chunk == null) return;
        spawnList.Add(new SpawnRequest { coord = coord, chunk = chunk, distance = distance });
    }

    public void EnqueueMeshApply(TerrainChunk chunk)
    {
        if (chunk != null) meshQueue.Enqueue(chunk);
    }

    public void EnqueueColliderAdd(TerrainChunk chunk, int distanceFromPlayer)
    {
        if (chunk == null) return;

        bool needs = distanceFromPlayer <= colliderDistance;
        chunk.needsCollider = needs;

        if (needs)
            colliderQueue.Enqueue(chunk);
    }

    public void EnqueueColliderRevoke(TerrainChunk chunk)
    {
        if (chunk != null) colliderRevokeQueue.Enqueue(chunk);
    }

    public void Clear()
    {
        spawnList.Clear();
        meshQueue.Clear();
        colliderQueue.Clear();
        colliderRevokeQueue.Clear();
    }

    private void ProcessSpawnQueue()
    {
        if (spawnList.Count == 0) return;

        spawnList.Sort((a, b) => a.distance.CompareTo(b.distance));

        int i = 0;
        while (i < spawnList.Count)
        {
            if (spawnsThisFrame >= maxSpawnsPerFrame) break;
            if (Time.unscaledTime - lastSpawnTime < minSpawnInterval) break;

            SpawnRequest req = spawnList[i];
            spawnList.RemoveAt(i);

            if (req.chunk == null || !req.chunk.gameObject.activeInHierarchy)
                continue;

            if (OnSpawnReady != null) OnSpawnReady.Invoke(req);
            spawnsThisFrame++;
            lastSpawnTime = Time.unscaledTime;
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

                // ─────────────────────────────────────────────────
                // CRITICAL FIX: After applying the visual mesh,
                // automatically enqueue the collider if needed.
                // Without this, the chunk sits in VisualReady state
                // forever with no collider, and the player falls through.
                // ─────────────────────────────────────────────────
                if (chunk.needsCollider)
                    colliderQueue.Enqueue(chunk);
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
            if (chunk.state != ChunkState.VisualReady) continue;
            if (!chunk.needsCollider) continue;

            if (chunk.ApplyCollider())
                collidersThisFrame++;
        }
    }

    private void ProcessColliderRevokeQueue()
    {
        int safety = 4;
        while (colliderRevokeQueue.Count > 0 && revokesThisFrame < 2 && safety-- > 0)
        {
            TerrainChunk chunk = colliderRevokeQueue.Dequeue();
            if (chunk == null) continue;
            chunk.RevokeCollider();
            revokesThisFrame++;
        }
    }
}
