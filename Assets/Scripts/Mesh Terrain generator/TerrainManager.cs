// ============================================================
//  TerrainManager.cs  (v2)
//
//  No Man's Sky-style chunk management:
//
//  LOADING
//   - Chunks sorted by distance from player (nearest first)
//   - Only N chunks are STARTED per frame (maxGenerationsPerFrame)
//     so a burst of movement never causes a 1-frame spike.
//
//  VISIBILITY / SLEEPING
//   - viewDistance        – chunks that are visible and active
//   - sleepDistance       – chunks beyond view but MESH KEPT.
//     Player can walk back and the chunk wakes instantly (no
//     regeneration, zero stutter).
//
//  UNLOADING
//   - unloadDistance      – chunks truly beyond this are recycled
//     back to the pool and their mesh destroyed.
//   - Unload only triggers if chunk is outside unloadDistance AND
//     has been sleeping > minSleepFrames (hysteresis buffer).
//
//  GENERATION THROTTLE
//   - maxGenerationsPerFrame: how many new thread starts per frame.
//     Default 2 means even fast movement starts at most 2 threads/frame.
//
//  NO UnityEditor.TerrainTools dependency (fixes compile error).
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public class TerrainManager : MonoBehaviour
{
    // ── World ────────────────────────────────────────────────
    [Header("World")]
    public Transform  player;
    public int        chunkSize      = 257;     // verts per side (power-of-2 + 1)
    public int        chunkWorldSize = 256;     // world units per chunk
    public GameObject chunkPrefab;
    public Material   terrainMaterial;

    [Header("Streaming Distances  (in chunk units)")]
    [Tooltip("Chunks up to this distance are visible and active.")]
    [Range(1, 8)] public int viewDistance   = 3;

    [Tooltip("Chunks beyond viewDistance but within sleepDistance keep their mesh but are hidden.")]
    [Range(1, 12)] public int sleepDistance = 5;

    [Tooltip("Chunks beyond this distance are fully recycled (mesh destroyed).")]
    [Range(2, 16)] public int unloadDistance = 7;

    [Tooltip("A sleeping chunk must be outside unloadDistance for at least this many frames before recycling. Prevents oscillation.")]
    public int minSleepFrames = 120;

    [Tooltip("Max new chunk threads started per frame. Keep at 1-3 to prevent frame spikes.")]
    [Range(1, 6)] public int maxGenerationsPerFrame = 2;

    // ── Seed ─────────────────────────────────────────────────
    [Header("Seed")]
    public int seed = 42;

    // ── Terrain Shape ────────────────────────────────────────
    [Header("Terrain Shape")]
    public float baseFrequency   = 0.0018f;
    [Range(1,10)] public int octaves = 7;
    [Range(0.1f,0.9f)] public float persistence = 0.48f;
    [Range(1.2f,3.5f)] public float lacunarity  = 2.1f;
    public float heightMult      = 220f;   // bigger = taller mountains
    [Range(0.5f,4f)] public float exponent = 1.8f;

    [Header("Mountain")]
    public float mountainBlend   = 0.75f;
    public float mountainFreq    = 0.0018f;

    [Header("River")]
    public float riverFrequency  = 0.00038f;
    [Range(0.01f,0.15f)] public float riverWidth = 0.05f;
    public float riverDepth      = 18f;
    [Range(0.01f,0.25f)] public float bankWidth  = 0.10f;
    [Range(0f,1f)] public float bankFlatness     = 0.65f;

    [Header("Valley")]
    public float valleyFrequency = 0.0007f;
    [Range(0f,1f)] public float valleyDepth      = 0.40f;

    [Header("Vegetation")]
    [Range(0f, 0.15f)]
    public float vegetationDensity = 0.04f;

    // ── Internal ─────────────────────────────────────────────
    private float      _ox, _oz;
    private VegetationPlacer _vegPlacer;

    // All known chunks (active, sleeping, generating)
    private Dictionary<Vector2Int, TerrainChunk> _chunks
        = new Dictionary<Vector2Int, TerrainChunk>();

    // Sleep age counter: how many frames a chunk has been sleeping
    private Dictionary<Vector2Int, int> _sleepAge
        = new Dictionary<Vector2Int, int>();

    // Generation queue sorted by distance
    private List<Vector2Int> _genQueue = new List<Vector2Int>();

    private Queue<TerrainChunk> _pool   = new Queue<TerrainChunk>();
    private Vector2Int           _lastPlayerChunk
        = new Vector2Int(int.MaxValue, int.MaxValue);

    // ─────────────────────────────────────────────────────────
    void Start()
    {
        // Deterministic offsets from seed
        var rng = new System.Random(seed);
        _ox = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        _oz = (float)(rng.NextDouble() * 200000.0 - 100000.0);

        if (player == null && Camera.main != null)
            player = Camera.main.transform;

        _vegPlacer = GetComponent<VegetationPlacer>();
    }

    // ─────────────────────────────────────────────────────────
    void Update()
    {
        if (player == null) return;

        Vector2Int pc = WorldToChunk(player.position);

        // Re-evaluate chunk visibility whenever player moves
        if (pc != _lastPlayerChunk)
        {
            _lastPlayerChunk = pc;
            UpdateStreaming(pc);
        }

        // Tick all generating chunks (thread result poll)
        foreach (var kv in _chunks)
            kv.Value.Tick();

        // Drain generation queue: start at most N threads per frame
        int started = 0;
        for (int i = _genQueue.Count - 1; i >= 0 && started < maxGenerationsPerFrame; i--)
        {
            Vector2Int coord = _genQueue[i];
            if (!_chunks.ContainsKey(coord)) { _genQueue.RemoveAt(i); continue; }

            TerrainChunk c = _chunks[coord];
            if (c.State == ChunkState.Idle)
            {
                c.StartGeneration();
                started++;
            }
            _genQueue.RemoveAt(i);
        }
    }

    // ─────────────────────────────────────────────────────────
    void UpdateStreaming(Vector2Int center)
    {
        // ── 1. Decide the desired state for every chunk in range ──
        var needed = new HashSet<Vector2Int>();

        for (int dx = -unloadDistance; dx <= unloadDistance; dx++)
        for (int dz = -unloadDistance; dz <= unloadDistance; dz++)
        {
            var coord = new Vector2Int(center.x + dx, center.y + dz);
            int dist  = ChebyshevDist(coord, center);

            if (dist > unloadDistance) continue;
            needed.Add(coord);

            if (!_chunks.ContainsKey(coord))
            {
                // Brand new chunk
                TerrainChunk c = GetOrCreate(coord);
                _chunks[coord] = c;
                QueueGeneration(coord, dist);
            }
            else
            {
                TerrainChunk c = _chunks[coord];

                if (dist <= viewDistance)
                {
                    // Should be visible
                    if (c.State == ChunkState.Sleeping || c.State == ChunkState.Ready)
                        c.SetVisible(true);

                    _sleepAge.Remove(coord);
                }
                else if (dist <= sleepDistance)
                {
                    // Should be sleeping (mesh kept, hidden)
                    if (c.State == ChunkState.Active)
                        c.SetVisible(false);

                    _sleepAge.Remove(coord);
                }
                else
                {
                    // Outside sleepDistance – increment sleep age
                    _sleepAge.TryGetValue(coord, out int age);
                    _sleepAge[coord] = age + 1;
                }
            }
        }

        // ── 2. Recycle chunks outside unloadDistance or past sleep age ──
        var toRemove = new List<Vector2Int>();
        foreach (var kv in _chunks)
        {
            if (needed.Contains(kv.Key)) continue;

            _sleepAge.TryGetValue(kv.Key, out int age);
            if (age >= minSleepFrames)
            {
                kv.Value.Recycle();
                _pool.Enqueue(kv.Value);
                toRemove.Add(kv.Key);
                _sleepAge.Remove(kv.Key);
            }
        }
        foreach (var k in toRemove) _chunks.Remove(k);

        // ── 3. Sort generation queue by distance (nearest first) ──
        _genQueue.Sort((a, b) =>
            ChebyshevDist(a, center).CompareTo(ChebyshevDist(b, center)));
    }

    // ─────────────────────────────────────────────────────────
    void QueueGeneration(Vector2Int coord, int dist)
    {
        // Nearest chunks go to front of list (list is sorted in UpdateStreaming)
        if (!_genQueue.Contains(coord))
            _genQueue.Add(coord);
    }

    // ─────────────────────────────────────────────────────────
    TerrainChunk GetOrCreate(Vector2Int coord)
    {
        TerrainChunk c = null;

        // Drain pool
        while (_pool.Count > 0 && c == null)
            c = _pool.Dequeue();

        if (c == null)
        {
            var go = Instantiate(chunkPrefab, Vector3.zero, Quaternion.identity, transform);
            c = go.GetComponent<TerrainChunk>();
            if (c == null) c = go.AddComponent<TerrainChunk>();
        }

        // Configure
        c.coord         = coord;
        c.position      = new Vector2(coord.x, coord.y);
        c.chunkSize     = chunkSize;
        c.chunkWorldSize= chunkWorldSize;
        c.material      = terrainMaterial;
        c.vegPlacer     = _vegPlacer;

        c.baseFrequency = baseFrequency;
        c.octaves       = octaves;
        c.persistence   = persistence;
        c.lacunarity    = lacunarity;
        c.heightMult    = heightMult;
        c.exponent      = exponent;
        c.mountainBlend = mountainBlend;
        c.mountainFreq  = mountainFreq;
        c.riverFrequency= riverFrequency;
        c.riverWidth    = riverWidth;
        c.riverDepth    = riverDepth;
        c.bankWidth     = bankWidth;
        c.bankFlatness  = bankFlatness;
        c.valleyFrequency = valleyFrequency;
        c.valleyDepth   = valleyDepth;
        c.offsetX       = _ox;
        c.offsetZ       = _oz;
        c.vegDensity    = vegetationDensity;

        c.transform.position = new Vector3(
            coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);
        c.gameObject.SetActive(true);

        return c;
    }

    // ─────────────────────────────────────────────────────────
    Vector2Int WorldToChunk(Vector3 p) =>
        new Vector2Int(
            Mathf.FloorToInt(p.x / chunkWorldSize),
            Mathf.FloorToInt(p.z / chunkWorldSize));

    int ChebyshevDist(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    // ── Editor live-refresh ──────────────────────────────────
#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        foreach (var kv in _chunks) kv.Value.Recycle();
        _chunks.Clear();
        _sleepAge.Clear();
        _genQueue.Clear();
        _lastPlayerChunk = new Vector2Int(int.MaxValue, int.MaxValue);
        var rng = new System.Random(seed);
        _ox = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        _oz = (float)(rng.NextDouble() * 200000.0 - 100000.0);
    }
#endif
}
