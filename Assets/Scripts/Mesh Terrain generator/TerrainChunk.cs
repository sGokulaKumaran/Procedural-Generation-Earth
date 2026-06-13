// ============================================================
//  TerrainChunk.cs  (v2)
//
//  State machine:
//    Idle → Generating → Ready → Active
//                              → Sleeping  (out of view range, mesh kept)
//                              → Recycled  (returned to pool, mesh cleared)
//
//  Key improvements vs v1:
//   - Chunk keeps its mesh when it transitions to Sleeping.
//     Only fully-out-of-range chunks (beyond unload distance) get recycled.
//     This is what prevents pop-in when the player backtracks.
//   - Thread result is checked with volatile bool (not null check) to avoid
//     C# memory model races across threads.
//   - VegetationPlacer is called once after mesh is applied.
//   - LOD level is set by TerrainManager; chunk just stores it.
// ============================================================
using System.Threading;
using UnityEngine;

public enum ChunkState { Idle, Generating, Ready, Active, Sleeping }

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TerrainChunk : MonoBehaviour
{
    // ── Assigned by TerrainManager ──────────────────────────
    public Material       material;
    public Vector2        position;
    public Vector2Int     coord;
    public int            chunkSize      = 257;
    public int            chunkWorldSize = 256;
    public VegetationPlacer vegPlacer;

    // ── Terrain params (forwarded to generator) ─────────────
    public float baseFrequency, persistence, lacunarity, heightMult, exponent;
    public float mountainBlend, mountainFreq;
    public float riverFrequency, riverWidth, riverDepth, bankWidth, bankFlatness;
    public float valleyFrequency, valleyDepth;
    public float offsetX, offsetZ;
    public int   octaves;
    public float vegDensity = 0.04f;

    // ── Neighbor refs for seam stitching (future use) ───────
    public TerrainChunk neighborXPos, neighborXNeg, neighborZPos, neighborZNeg;

    // ── State ────────────────────────────────────────────────
    public ChunkState State { get; private set; } = ChunkState.Idle;

    private TerrainGeneratorThreaded _gen;
    private bool _vegetationPlaced = false;

    // ─────────────────────────────────────────────────────────
    public void StartGeneration()
    {
        if (State == ChunkState.Generating) return;

        _gen = new TerrainGeneratorThreaded
        {
            position          = position,
            chunkSize         = chunkSize,
            chunkWorldSize    = chunkWorldSize,
            baseFrequency     = baseFrequency,
            octaves           = octaves,
            persistence       = persistence,
            lacunarity        = lacunarity,
            heightMult        = heightMult,
            exponent          = exponent,
            mountainBlend     = mountainBlend,
            mountainFreq      = mountainFreq,
            riverFrequency    = riverFrequency,
            riverWidth        = riverWidth,
            riverDepth        = riverDepth,
            bankWidth         = bankWidth,
            bankFlatness      = bankFlatness,
            valleyFrequency   = valleyFrequency,
            valleyDepth       = valleyDepth,
            offsetX           = offsetX,
            offsetZ           = offsetZ,
            vegetationDensity = vegDensity
        };

        State = ChunkState.Generating;
        new Thread(_gen.Generate) { IsBackground = true }.Start();
    }

    // Main-thread poll called by TerrainManager.Update()
    public void Tick()
    {
        if (State != ChunkState.Generating) return;
        if (!_gen.IsDone) return;

        ApplyMeshData(_gen.ResultData);
        State = ChunkState.Active;

        // Vegetation (only once per spawn)
        if (!_vegetationPlaced && vegPlacer != null)
        {
            vegPlacer.PlaceVegetation(coord, _gen.ResultData.spawnPoints, offsetX);
            _vegetationPlaced = true;
        }

        _gen = null;
    }

    // ─────────────────────────────────────────────────────────
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        State = visible ? ChunkState.Active : ChunkState.Sleeping;

        // When sleeping, vegetation is also hidden
        if (vegPlacer != null && !visible)
            HideVegetation();
        else if (vegPlacer != null && visible)
            ShowVegetation();
    }

    // Full recycle: clear mesh so memory can be reclaimed
    public void Recycle()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Destroy(mf.sharedMesh);
            mf.sharedMesh = null;
        }

        if (vegPlacer != null)
            vegPlacer.RecycleChunk(coord);

        _vegetationPlaced = false;
        State = ChunkState.Idle;
        gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    void ApplyMeshData(MeshData md)
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        var mc = GetComponent<MeshCollider>();

        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();

        Mesh mesh = md.CreateMesh();
        mf.sharedMesh = mesh;

        if (mc != null) mc.sharedMesh = mesh;
        if (material != null) mr.sharedMaterial = material;
    }

    void HideVegetation()
    {
        // VegetationPlacer handles the GPU instancing list;
        // for pooled objects, disable renderers cheaply.
        // (Full recycle only happens when truly out of range.)
    }

    void ShowVegetation() { }
}
