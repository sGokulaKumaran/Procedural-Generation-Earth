using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Caches the canonical (always full-resolution) terrain data for chunk coordinates.
///
/// Replaces LRUHeightmapCache. Two bugs are fixed here:
///  1. Thread safety: the old cache had no locking at all, yet Put() was called from a
///     background generation thread while Get() was called from the main thread. Concurrent
///     Dictionary/LinkedList access with no synchronization can corrupt internal state.
///     Every entry point here is behind a single lock.
///  2. Resolution tracking: the old cache stored whatever resolution a given tier happened
///     to generate at, but callers assumed it was always full resolution when bilinear
///     sampling it — silently reading wrong array indices when that wasn't true. Every entry
///     here records its own resolution, and this cache is only ever fed full-resolution data
///     in the first place (see ChunkManager), so downstream sampling is always safe.
/// </summary>
public class ChunkDataCache
{
    public class Entry
    {
        public Vector2Int coord;
        public int resolution;
        public float[] height;
        public float[] flatness;
        public Vector3[] normals;
    }

    public int capacity = 96;

    readonly object gate = new object();
    readonly LinkedList<Entry> lru = new LinkedList<Entry>();
    readonly Dictionary<Vector2Int, LinkedListNode<Entry>> index = new Dictionary<Vector2Int, LinkedListNode<Entry>>();

    public bool TryGet(Vector2Int coord, out Entry entry)
    {
        lock (gate)
        {
            if (index.TryGetValue(coord, out var node))
            {
                lru.Remove(node);
                lru.AddFirst(node);
                entry = node.Value;
                return true;
            }
        }
        entry = null;
        return false;
    }

    public bool Contains(Vector2Int coord)
    {
        lock (gate)
        {
            return index.ContainsKey(coord);
        }
    }

    public void Put(Vector2Int coord, int resolution, float[] height, float[] flatness, Vector3[] normals)
    {
        lock (gate)
        {
            if (index.TryGetValue(coord, out var existing))
            {
                existing.Value.resolution = resolution;
                existing.Value.height = height;
                existing.Value.flatness = flatness;
                existing.Value.normals = normals;
                lru.Remove(existing);
                lru.AddFirst(existing);
                return;
            }

            var entry = new Entry
            {
                coord = coord,
                resolution = resolution,
                height = height,
                flatness = flatness,
                normals = normals,
            };
            var node = new LinkedListNode<Entry>(entry);
            lru.AddFirst(node);
            index[coord] = node;

            while (lru.Count > capacity)
            {
                var old = lru.Last;
                lru.RemoveLast();
                index.Remove(old.Value.coord);
            }
        }
    }

    public void Invalidate(Vector2Int coord)
    {
        lock (gate)
        {
            if (index.TryGetValue(coord, out var node))
            {
                lru.Remove(node);
                index.Remove(coord);
            }
        }
    }
}