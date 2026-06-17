// ============================================================
//  TerrainChunk.cs  (UPDATED for LOD-style throttled loading)
//
//  State machine:
//    Idle        -> fresh / pooled, no mesh
//    Generating  -> worker thread running on background
//    MeshPending -> worker thread done, waiting for scheduler to apply
//    VisualReady -> MeshFilter.mesh applied, waiting for collider
//    Complete    -> mesh + (optional) collider applied
//
//  Generation results are passed through a `GenerationBox` wrapper
//  so a stale worker thread can never overwrite a fresh generation.
// ============================================================
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TerrainChunk : MonoBehaviour
{
    public enum ChunkState
    {
        Idle,
        Generating,
        MeshPending,
        VisualReady,
        Complete
    }

    public Material material;
    public Vector2 position;
    public int chunkSize = 256;
    public int chunkWorldSize = 256;

    // ── Neighbors ──
    public TerrainChunk neighborXPos;
    public TerrainChunk neighborXNeg;
    public TerrainChunk neighborZPos;
    public TerrainChunk neighborZNeg;

    // ── Noise parameters (copied from manager each spawn) ──
    public float baseFrequency = 0.0018f;
    public int octaves = 7;
    public float persistence = 0.48f;
    public float lacunarity = 2.1f;
    public float heightMult = 120f;
    public float exponent = 1.6f;
    public float mountainBlend = 0.55f;
    public float mountainFreq = 0.0022f;
    public float riverFrequency = 0.00045f;
    public float riverWidth = 0.045f;
    public float riverDepth = 12f;
    public float bankWidth = 0.09f;
    public float bankFlatness = 0.60f;
    public float valleyFrequency = 0.0008f;
    public float valleyDepth = 0.35f;
    public float offsetX = 0f;
    public float offsetZ = 0f;

    // ── LOD / state ──
    public bool needsCollider = true;
    public ChunkState state = ChunkState.Idle;

    // Cached components
    private MeshFilter mf;
    private MeshRenderer mr;
    private MeshCollider mc;

    // Holds the latest finished mesh data, set by the worker thread
    private MeshData pendingMeshData;

    // Wrapper so the worker thread can write to *its own* box without
    // racing with a new generation on the same chunk.
    private class GenerationBox { public MeshData data; }
    private GenerationBox currentBox = new GenerationBox();

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        mc = GetComponent<MeshCollider>();
    }

    void Update()
    {
        // Skip chunks that have been recycled
        if (!gameObject.activeInHierarchy) return;

        // Worker thread finished? Transition to MeshPending and notify scheduler.
        if (state == ChunkState.Generating && currentBox.data != null)
        {
            pendingMeshData = currentBox.data;
            state = ChunkState.MeshPending;

            ChunkLoadScheduler scheduler = FindScheduler();
            if (scheduler != null)
                scheduler.EnqueueMeshApply(this);
            else
                ApplyVisualMesh(); // fallback (no scheduler in scene)
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Generation
    // ─────────────────────────────────────────────────────────────

    public void StartThreadedGeneration()
    {
        // Allocate a fresh box; any orphan write from a previous generation
        // goes to its own (now-unreferenced) box and is silently dropped.
        var box = new GenerationBox();
        currentBox = box;
        pendingMeshData = null;
        state = ChunkState.Generating;

        TerrainGeneratorThreaded gen = new TerrainGeneratorThreaded();
        gen.position = position;
        gen.chunkSize = chunkSize;
        gen.chunkWorldSize = chunkWorldSize;
        gen.baseFrequency = baseFrequency;
        gen.persistence = persistence;
        gen.lacunarity = lacunarity;
        gen.heightMult = heightMult;
        gen.exponent = exponent;
        gen.mountainBlend = mountainBlend;
        gen.mountainFreq = mountainFreq;
        gen.riverFrequency = riverFrequency;
        gen.riverWidth = riverWidth;
        gen.riverDepth = riverDepth;
        gen.bankWidth = bankWidth;
        gen.bankFlatness = bankFlatness;
        gen.valleyFrequency = valleyFrequency;
        gen.valleyDepth = valleyDepth;
        gen.offsetX = offsetX;
        gen.offsetZ = offsetZ;

        Thread thread = new Thread(() =>
        {
            gen.Generate();
            // Write to the local box — never collides with a new generation
            box.data = gen.ResultData;
        });
        thread.IsBackground = true;
        thread.Start();
    }

    // ─────────────────────────────────────────────────────────────
    //  Application (called by the scheduler, throttled)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Step 1: apply the MeshFilter mesh (moderate cost).</summary>
    public bool ApplyVisualMesh()
    {
        if (state != ChunkState.MeshPending || pendingMeshData == null) return false;

        Mesh mesh = pendingMeshData.CreateMesh();
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mr == null) mr = GetComponent<MeshRenderer>();
        mf.mesh = mesh;
        if (material != null) mr.sharedMaterial = material;

        pendingMeshData = null;
        state = ChunkState.VisualReady;
        return true;
    }

    /// <summary>Step 2: apply the MeshCollider (expensive — biggest hitch).</summary>
    public bool ApplyCollider()
    {
        if (state != ChunkState.VisualReady) return false;
        if (mc == null) mc = GetComponent<MeshCollider>();
        if (mc == null || mf == null || mf.sharedMesh == null)
        {
            state = ChunkState.Complete;
            return false;
        }

        // The single line that causes most chunk-loading hitches.
        // Spreading these calls across frames is what eliminates the FPS drops.
        mc.sharedMesh = mf.sharedMesh;
        state = ChunkState.Complete;
        return true;
    }

    public bool HasMeshCollider() => mc != null;

    // ─────────────────────────────────────────────────────────────
    //  Pool reset (called when the chunk is recycled)
    // ─────────────────────────────────────────────────────────────

    public void ResetForPool()
    {
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mc == null) mc = GetComponent<MeshCollider>();

        // Drop collider reference BEFORE destroying the mesh
        if (mc != null) mc.sharedMesh = null;

        if (mf != null && mf.mesh != null)
        {
            if (Application.isPlaying) Destroy(mf.mesh);
            else DestroyImmediate(mf.mesh);
            mf.mesh = null;
        }

        // Orphan any in-flight worker thread by replacing the box
        currentBox = new GenerationBox();
        pendingMeshData = null;
        state = ChunkState.Idle;
        needsCollider = true;

        neighborXPos = neighborXNeg = neighborZPos = neighborZNeg = null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private ChunkLoadScheduler FindScheduler()
    {
        TerrainManager mgr = GetComponentInParent<TerrainManager>();
        return mgr != null ? mgr.scheduler : null;
    }
}
