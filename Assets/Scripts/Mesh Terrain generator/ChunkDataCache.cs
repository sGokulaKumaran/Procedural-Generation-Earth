// =============================================================================
// ChunkDataCache.cs
//
// Thread-safe LRU cache that stores one canonical (full-resolution) heightmap,
// flatness map, and normal array per chunk coordinate.
//
// Why this exists:
//   ChunkTerrainGenerator produces per-chunk data at the SAME resolution for
//   every chunk (fullVerts). ChunkMeshBuilder can then resample that data to
//   any LOD tier (near/mid/far) with pure array math, so once a chunk is in
//   the cache, rebuilding its mesh at a different LOD is essentially free
//   and safe to do on the main thread.
//
//   Without the cache, every LOD tier change would force a full background
//   regeneration from noise — expensive and laggy.
//
// Why an LRU (not a plain Dictionary):
//   A finite world is huge; we can't hold every coordinate's full-res data
//   in memory. The LRU evicts the least-recently-used entries past a
//   configurable capacity (ChunkManager sets it to (2*farRadius+1)^2 so the
//   working set never thrashes). When an evicted coordinate is needed again,
//   it just re-runs the background generation (rare, only when the player
//   re-visits a distant area or when live-editing).
//
// Thread-safety:
//   The cache is read by the main thread (ChunkManager.RequestChunkMesh and
//   SyncActiveColliders) AND written by background threads
//   (ChunkGenerationScheduler worker -> ChunkTerrainGenerator). Every public
//   method is locked on `gate`. TryGet returns a reference to the internal
//   arrays, so callers must treat them as read-only.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class ChunkDataCache
{
    /// <summary>
    /// Max number of coordinate entries to keep. Set by ChunkManager
    /// (auto-sized to the working set in Start). When the cache is full,
    /// the least-recently-used entry is evicted.
    /// </summary>
    public int capacity = 256;

    // ── backing data ──
    // Indexed by coord hash. Each dictionary's value lives for as long as the
    // entry is in the cache, so TryGet returns refs to arrays that the caller
    // must NOT modify.
    readonly Dictionary<Vector2Int, float[]> heights = new Dictionary<Vector2Int, float[]>();
    readonly Dictionary<Vector2Int, float[]> flatness = new Dictionary<Vector2Int, float[]>();
    readonly Dictionary<Vector2Int, Vector3[]> normals = new Dictionary<Vector2Int, Vector3[]>();
    readonly Dictionary<Vector2Int, int> resolutions = new Dictionary<Vector2Int, int>();

    // ── LRU bookkeeping ──
    // Touch order: head = most recently used, tail = least recently used.
    readonly LinkedList<Vector2Int> lruList = new LinkedList<Vector2Int>();
    readonly Dictionary<Vector2Int, LinkedListNode<Vector2Int>> lruNodes =
        new Dictionary<Vector2Int, LinkedListNode<Vector2Int>>();

    readonly object gate = new object();

    /// <summary>
    /// Read-only entry returned by TryGet. Treat all array fields as
    /// read-only — mutating them will break any other chunk that's looking
    /// at the same cached coordinate.
    /// </summary>
    public struct Entry
    {
        public float[] height;
        public float[] flatness;
        public Vector3[] normals;
        public int resolution;
    }

    public bool TryGet(Vector2Int coord, out Entry entry)
    {
        lock (gate)
        {
            if (heights.TryGetValue(coord, out var h) &&
                flatness.TryGetValue(coord, out var f) &&
                normals.TryGetValue(coord, out var n) &&
                resolutions.TryGetValue(coord, out var res))
            {
                // Touch LRU position — most recent at head.
                if (lruNodes.TryGetValue(coord, out var node))
                {
                    lruList.Remove(node);
                    lruList.AddFirst(node);
                }
                else
                {
                    // Defensive: entry data exists but LRU node was lost.
                    // Re-insert at head.
                    lruNodes[coord] = lruList.AddFirst(coord);
                }

                entry = new Entry
                {
                    height = h,
                    flatness = f,
                    normals = n,
                    resolution = res,
                };
                return true;
            }

            entry = default;
            return false;
        }
    }

    public void Put(Vector2Int coord, int resolution, float[] height, float[] flatness, Vector3[] normals)
    {
        if (height == null || flatness == null || normals == null) return;

        lock (gate)
        {
            // If this coord is already cached, replace in place and bump to MRU.
            if (heights.ContainsKey(coord))
            {
                heights[coord] = height;
                this.flatness[coord] = flatness;
                this.normals[coord] = normals;
                resolutions[coord] = resolution;

                if (lruNodes.TryGetValue(coord, out var node))
                {
                    lruList.Remove(node);
                    lruList.AddFirst(node);
                }
                else
                {
                    lruNodes[coord] = lruList.AddFirst(coord);
                }
                return;
            }

            // New entry — insert and evict from LRU tail if over capacity.
            heights[coord] = height;
            this.flatness[coord] = flatness;
            this.normals[coord] = normals;
            resolutions[coord] = resolution;
            lruNodes[coord] = lruList.AddFirst(coord);

            EvictIfOverCapacity();
        }
    }

    void EvictIfOverCapacity()
    {
        int cap = capacity < 1 ? 1 : capacity;
        while (lruList.Count > cap)
        {
            var tail = lruList.Last;
            if (tail == null) break;
            Vector2Int evicted = tail.Value;
            lruList.RemoveLast();
            lruNodes.Remove(evicted);
            heights.Remove(evicted);
            flatness.Remove(evicted);
            normals.Remove(evicted);
            resolutions.Remove(evicted);
        }
    }

    /// <summary>
    /// Drops every cached coordinate. Used by the live-edit system in
    /// ChunkManager when the user changes noise parameters in the inspector —
    /// all existing data was generated with the old parameters and is now
    /// stale, so we wipe and let the streaming system regenerate.
    ///
    /// Thread-safe: callable from the main thread; will not corrupt in-flight
    /// background workers because the worker's Put() will simply re-insert
    /// into a now-empty cache (the data it puts is up to date).
    /// </summary>
    public void Clear()
    {
        lock (gate)
        {
            heights.Clear();
            flatness.Clear();
            normals.Clear();
            resolutions.Clear();
            lruList.Clear();
            lruNodes.Clear();
        }
    }
}
