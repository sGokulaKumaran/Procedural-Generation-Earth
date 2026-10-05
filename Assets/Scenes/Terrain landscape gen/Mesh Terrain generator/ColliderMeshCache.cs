// =============================================================================
// ColliderMeshCache.cs
//
// LRU cache for low-resolution collider meshes, keyed by chunk coordinate.
// Built lazily on first collider request, reused for the cache's lifetime.
//
// Why this exists:
//   The previous code reused the visual mesh as the collider. Every LOD swap
//   re-baked the MeshCollider from a 256x256 mesh = 131k tris = 50-200ms spike
//   per chunk. With a dedicated 64-vert collider mesh baked ONCE per coordinate
//   and cached, MeshCollider bakes are < 5ms and happen at most once per
//   (coordinate, cache lifetime) — not on every visual update.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class ColliderMeshCache
{
    public int capacity = 64;

    readonly Dictionary<Vector2Int, Mesh> meshes = new Dictionary<Vector2Int, Mesh>();
    readonly LinkedList<Vector2Int> lruList = new LinkedList<Vector2Int>();
    readonly Dictionary<Vector2Int, LinkedListNode<Vector2Int>> lruNodes =
        new Dictionary<Vector2Int, LinkedListNode<Vector2Int>>();
    readonly object gate = new object();

    public bool TryGet(Vector2Int coord, out Mesh mesh)
    {
        lock (gate)
        {
            if (meshes.TryGetValue(coord, out mesh))
            {
                if (lruNodes.TryGetValue(coord, out var node))
                {
                    lruList.Remove(node);
                    lruList.AddLast(node);
                }
                return true;
            }
            return false;
        }
    }

    public void Put(Vector2Int coord, Mesh mesh)
    {
        if (mesh == null) return;
        lock (gate)
        {
            if (meshes.TryGetValue(coord, out var existing))
            {
                if (existing == mesh) return;
                meshes[coord] = mesh;
                if (lruNodes.TryGetValue(coord, out var node))
                {
                    lruList.Remove(node);
                    lruList.AddLast(coord);
                    lruNodes[coord] = lruList.Last;
                }
                SafeDestroy(existing);
                return;
            }

            meshes[coord] = mesh;
            lruNodes[coord] = lruList.AddLast(coord);

            while (lruList.Count > capacity)
            {
                var oldest = lruList.First;
                if (oldest == null) break;
                var oldCoord = oldest.Value;
                lruList.RemoveFirst();
                lruNodes.Remove(oldCoord);
                if (meshes.TryGetValue(oldCoord, out var oldMesh))
                {
                    meshes.Remove(oldCoord);
                    SafeDestroy(oldMesh);
                }
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            foreach (var m in meshes.Values) SafeDestroy(m);
            meshes.Clear();
            lruList.Clear();
            lruNodes.Clear();
        }
    }

    static void SafeDestroy(Mesh m)
    {
        if (m == null) return;
        if (Application.isPlaying) Object.Destroy(m);
        else Object.DestroyImmediate(m);
    }
}
