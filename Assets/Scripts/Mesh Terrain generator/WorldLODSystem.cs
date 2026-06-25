// ============================================================
//  WorldLODSystem.cs
//  Pre-generates a large world once, then manages visibility
//  based on player position. Chunks fade in/out smoothly.
//
//  This mimics No Man's Sky style LOD:
//   - World is generated up front (huge size)
//   - Only chunks within view distance are visible
//   - Distant chunks are hidden but kept in memory
//   - When player approaches, chunks fade in
//
//  Architecture:
//   - WorldLODSystem owns the TerrainManager
//   - Maintains a separate "background" pool of pre-generated chunks
//   - When player approaches, chunks swap from background to active
// ============================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldLODSystem : MonoBehaviour
{
    [Header("World Size")]
    [Tooltip("Total world radius in chunks. World is pre-generated to this size.")]
    [Range(10, 100)] public int worldRadiusChunks = 30;

    [Header("Visibility")]
    [Tooltip("Chunks within this radius of player are always visible.")]
    [Range(2, 20)] public int activeRadius = 4;

    [Tooltip("Chunks between activeRadius and this radius fade in/out.")]
    [Range(3, 30)] public int fadeRadius = 8;

    [Header("Fade Settings")]
    [Range(0.1f, 3f)] public float fadeInDuration = 0.5f;
    [Range(0.1f, 3f)] public float fadeOutDuration = 0.8f;

    [Header("Pre-Generation")]
    [Tooltip("How many chunks to generate per second during pre-gen.")]
    [Range(1, 50)] public int chunksPerSecond = 8;

    [Header("References")]
    public TerrainManager terrainManager;
    public Transform player;

    // ── Internal state ──
    private Dictionary<Vector2Int, TerrainChunk> allChunks = new Dictionary<Vector2Int, TerrainChunk>();
    private HashSet<Vector2Int> preGenComplete = new HashSet<Vector2Int>();
    private bool isPreGenerating = false;

    // For fade effect — track per-chunk fade state
    private Dictionary<TerrainChunk, FadeState> fadeStates = new Dictionary<TerrainChunk, FadeState>();

    private class FadeState
    {
        public float currentAlpha;     // 0 to 1
        public float targetAlpha;      // 0 or 1
        public bool isFading;
    }

    // ----------------------------------------------------------
    //  Lifecycle
    // ----------------------------------------------------------

    void Start()
    {
        if (terrainManager == null)
            terrainManager = GetComponent<TerrainManager>();
        if (player == null && Camera.main != null)
            player = Camera.main.transform;

        StartCoroutine(PreGenerateWorld());
    }

    void Update()
    {
        if (player == null || !isPreGenerating == false && preGenComplete.Count == 0) return;
        UpdateLODVisibility();
        UpdateFadeStates();
    }

    // ----------------------------------------------------------
    //  Pre-Generation Phase
    //  Generates the entire world once, in background
    // ----------------------------------------------------------

    IEnumerator PreGenerateWorld()
    {
        isPreGenerating = true;

        Vector2Int center = WorldToChunkCoord(player != null ? player.position : Vector3.zero);
        int total = (worldRadiusChunks * 2 + 1) * (worldRadiusChunks * 2 + 1);
        int generated = 0;
        float startTime = Time.realtimeSinceStartup;

        // Sort by distance from center so player area is ready first
        List<Vector2Int> coords = new List<Vector2Int>();
        for (int dx = -worldRadiusChunks; dx <= worldRadiusChunks; dx++)
            for (int dz = -worldRadiusChunks; dz <= worldRadiusChunks; dz++)
                coords.Add(new Vector2Int(center.x + dx, center.y + dz));

        coords.Sort((a, b) =>
        {
            int da = Mathf.Max(Mathf.Abs(a.x - center.x), Mathf.Abs(a.y - center.y));
            int db = Mathf.Max(Mathf.Abs(b.x - center.x), Mathf.Abs(b.y - center.y));
            return da.CompareTo(db);
        });

        // Generate in batches
        int batchSize = chunksPerSecond;
        foreach (var coord in coords)
        {
            PreGenerateChunk(coord, center);
            generated++;

            if (generated % batchSize == 0)
                yield return new WaitForEndOfFrame();
        }

        float elapsed = Time.realtimeSinceStartup - startTime;
        Debug.Log($"Pre-generation complete: {generated} chunks in {elapsed:F1}s");

        isPreGenerating = false;
    }

    void PreGenerateChunk(Vector2Int coord, Vector2Int playerChunk)
    {
        if (allChunks.ContainsKey(coord)) return;

        TerrainChunk chunk = terrainManager.GetOrCreateChunk();
        chunk.ResetForPool();

        // Position
        chunk.transform.position = new Vector3(
            coord.x * terrainManager.chunkWorldSize, 0f,
            coord.y * terrainManager.chunkWorldSize);

        chunk.gameObject.SetActive(true);
        chunk.SetVisible(false); // Hide during pre-gen

        // Copy generation parameters
        CopyGenParams(chunk);

        // Store the chunk data (we'll start generation)
        allChunks[coord] = chunk;
    }

    void CopyGenParams(TerrainChunk chunk)
    {
        // Pull parameters from terrainManager
        chunk.chunkWorldSize = terrainManager.chunkWorldSize;
        chunk.chunkSize = terrainManager.chunkSize;
        chunk.baseFrequency = terrainManager.baseFrequency;
        chunk.octaves = terrainManager.octaves;
        chunk.persistence = terrainManager.persistence;
        chunk.lacunarity = terrainManager.lacunarity;
        chunk.heightMult = terrainManager.heightMult;
        chunk.exponent = terrainManager.exponent;
        chunk.mountainBlend = terrainManager.mountainBlend;
        chunk.mountainFreq = terrainManager.mountainFreq;
        chunk.riverFrequency = terrainManager.riverFrequency;
        chunk.riverWidth = terrainManager.riverWidth;
        chunk.riverDepth = terrainManager.riverDepth;
        chunk.bankWidth = terrainManager.bankWidth;
        chunk.bankFlatness = terrainManager.bankFlatness;
        chunk.valleyFrequency = terrainManager.valleyFrequency;
        chunk.valleyDepth = terrainManager.valleyDepth;
        chunk.offsetX = terrainManager.GetOffsetX();
        chunk.offsetZ = terrainManager.GetOffsetZ();
    }

    // ----------------------------------------------------------
    //  LOD Visibility Update
    //  Decides which chunks should be visible based on player pos
    // ----------------------------------------------------------

    void UpdateLODVisibility()
    {
        Vector2Int playerChunk = WorldToChunkCoord(player.position);

        foreach (var kv in allChunks)
        {
            Vector2Int coord = kv.Key;
            TerrainChunk chunk = kv.Value;

            if (chunk == null) continue;

            int dist = Chebyshev(coord, playerChunk);

            if (dist <= activeRadius)
            {
                // Always visible — make sure it's fully shown
                SetChunkTargetAlpha(chunk, 1f, instant: false);
            }
            else if (dist <= fadeRadius)
            {
                // Fade zone — proportional visibility
                float alpha = 1f - Mathf.InverseLerp(activeRadius, fadeRadius, dist);
                SetChunkTargetAlpha(chunk, alpha, instant: false);
            }
            else
            {
                // Hidden — completely invisible
                SetChunkTargetAlpha(chunk, 0f, instant: false);
            }
        }
    }

    void SetChunkTargetAlpha(TerrainChunk chunk, float alpha, bool instant)
    {
        if (!fadeStates.TryGetValue(chunk, out FadeState state))
        {
            state = new FadeState
            {
                currentAlpha = instant ? alpha : 0f,
                targetAlpha = alpha,
                isFading = !instant
            };
            fadeStates[chunk] = state;
        }
        else
        {
            state.targetAlpha = alpha;
            if (instant) state.currentAlpha = alpha;
            state.isFading = state.currentAlpha != state.targetAlpha;
        }
    }

    void UpdateFadeStates()
    {
        // Update alpha over time
        List<TerrainChunk> toRemove = new List<TerrainChunk>();

        foreach (var kv in fadeStates)
        {
            TerrainChunk chunk = kv.Key;
            FadeState state = kv.Value;

            if (chunk == null) { toRemove.Add(chunk); continue; }

            if (state.isFading)
            {
                float duration = state.targetAlpha > state.currentAlpha ? fadeInDuration : fadeOutDuration;
                float speed = 1f / Mathf.Max(0.01f, duration);

                if (state.targetAlpha > state.currentAlpha)
                    state.currentAlpha = Mathf.Min(state.targetAlpha, state.currentAlpha + speed * Time.deltaTime);
                else
                    state.currentAlpha = Mathf.Max(state.targetAlpha, state.currentAlpha - speed * Time.deltaTime);

                if (Mathf.Approximately(state.currentAlpha, state.targetAlpha))
                    state.isFading = false;

                ApplyAlpha(chunk, state.currentAlpha);
            }

            // Hide completely if alpha is 0
            if (state.currentAlpha <= 0.001f && !state.isFading)
            {
                if (chunk.gameObject.activeSelf)
                    chunk.SetVisible(false);
            }
        }
    }

    void ApplyAlpha(TerrainChunk chunk, float alpha)
    {
        // Set material color alpha
        MeshRenderer mr = chunk.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null)
        {
            Color c = mr.material.color;
            c.a = alpha;
            mr.material.color = c;
        }
    }

    // ----------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------

    Vector2Int WorldToChunkCoord(Vector3 pos) => new Vector2Int(
        Mathf.FloorToInt(pos.x / terrainManager.chunkWorldSize),
        Mathf.FloorToInt(pos.z / terrainManager.chunkWorldSize));

    static int Chebyshev(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
}
