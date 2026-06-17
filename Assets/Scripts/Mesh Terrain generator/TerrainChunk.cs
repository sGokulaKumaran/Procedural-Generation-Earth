using System.Threading;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
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

    public TerrainChunk neighborXPos;
    public TerrainChunk neighborXNeg;
    public TerrainChunk neighborZPos;
    public TerrainChunk neighborZNeg;

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

    public bool needsCollider = true;
    public ChunkState state = ChunkState.Idle;

    private MeshFilter mf;
    private MeshRenderer mr;
    private MeshCollider mc;

    private MeshData pendingMeshData;

    private class GenerationBox { public MeshData data; }
    private GenerationBox currentBox = new GenerationBox();

    // ────────────────────────────────────────────
    //  Lifecycle
    // ────────────────────────────────────────────

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        mc = GetComponent<MeshCollider>();
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;

        if (state == ChunkState.Generating && currentBox.data != null)
        {
            pendingMeshData = currentBox.data;
            state = ChunkState.MeshPending;

            ChunkLoadScheduler scheduler = FindScheduler();
            if (scheduler != null)
                scheduler.EnqueueMeshApply(this);
            else
                ApplyVisualMesh();
        }
    }

    // ────────────────────────────────────────────
    //  Generation
    // ────────────────────────────────────────────

    public void StartThreadedGeneration()
    {
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
            box.data = gen.ResultData;
        });
        thread.IsBackground = true;
        thread.Start();
    }

    // ────────────────────────────────────────────
    //  Application (called by scheduler, throttled)
    // ────────────────────────────────────────────

    /// <summary>Apply the MeshFilter mesh. Cheap-ish.</summary>
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

    /// <summary>Apply the MeshCollider. EXPENSIVE — this is what causes hitches.</summary>
    public bool ApplyCollider()
    {
        if (state != ChunkChunkStateProxy()) return false; // helper below
        if (mc == null) mc = GetComponent<MeshCollider>();
        if (mf == null || mf.sharedMesh == null)
        {
            state = ChunkState.Complete;
            return false;
        }

        mc.sharedMesh = mf.sharedMesh;
        state = ChunkState.Complete;
        return true;
    }

    // tiny helper so the line above reads cleanly
    private ChunkState ChunkChunkStateProxy() => ChunkState.VisualReady;

    /// <summary>Cheap: just drop the collider reference. Safe to do on main thread.</summary>
    public bool RevokeCollider()
    {
        if (mc == null) return false;
        if (mc.sharedMesh == null) return false;

        mc.sharedMesh = null;        // drops BVH; no rebuild cost
        needsCollider = false;
        return true;
    }

    public bool HasMeshCollider() => mc != null;

    // ────────────────────────────────────────────
    //  Pool reset
    // ────────────────────────────────────────────

    public void ResetForPool()
    {
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mc == null) mc = GetComponent<MeshCollider>();

        if (mc != null) mc.sharedMesh = null;        // drop BEFORE destroying
        if (mf != null && mf.mesh != null)
        {
            if (Application.isPlaying) Destroy(mf.mesh);
            else DestroyImmediate(mf.mesh);
            mf.mesh = null;
        }

        currentBox = new GenerationBox();
        pendingMeshData = null;
        state = ChunkState.Idle;
        needsCollider = true;

        neighborXPos = neighborXNeg = neighborZPos = neighborZNeg = null;
    }

    // ────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────

    private ChunkLoadScheduler FindScheduler()
    {
        TerrainManager mgr = GetComponentInParent<TerrainManager>();
        return mgr != null ? mgr.scheduler : null;
    }
}
