// ============================================================
//  TerrainGeneratorThreaded.cs  (v2)
//
//  Runs entirely on a background thread. Main-thread Unity API
//  calls (Instantiate, GetComponent, etc.) are NOT allowed here.
//  Only Mathf.* and pure C# are safe.
//
//  What's new vs v1:
//   - Full river + valley + continent mask height pipeline
//     (delegates to NoiseUtility.SampleHeight for DRY sharing
//     with the vegetation placer)
//   - Slope computed per-vertex for cliff detection and biome
//   - Vegetation spawn point list built here on the thread,
//     ready for VegetationPlacer to consume on the main thread
//   - volatile bool IsDone used instead of null-check on ResultData
//     (safer with C# memory model across threads)
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public class TerrainGeneratorThreaded
{
    // ── Inputs ───────────────────────────────────────────────
    public Vector2 position;
    public int     chunkSize;
    public int     chunkWorldSize;

    public float baseFrequency, persistence, lacunarity, heightMult, exponent;
    public float mountainBlend, mountainFreq;
    public float riverFrequency, riverWidth, riverDepth, bankWidth, bankFlatness;
    public float valleyFrequency, valleyDepth;
    public float offsetX, offsetZ;
    public int   octaves;

    // Vegetation density: 0..1. Higher = more spawn candidates sampled.
    public float vegetationDensity = 0.04f;   // ~4 % of vertices become candidates

    // ── Outputs ──────────────────────────────────────────────
    public volatile bool    IsDone    = false;
    public MeshData         ResultData;

    // ─────────────────────────────────────────────────────────

  

    public void Generate()
    {
        MeshData md = new MeshData(chunkSize);
        int vi   = 0;
        int tris = 0;

        // ── PASS 1: height + colour ───────────────────────────
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                float wx = WorldX(x);
                float wz = WorldZ(z);

                float y = SampleH(wx, wz);
                md.vertices[vi] = new Vector3(x * ScaleX(), y, z * ScaleZ());
                md.uvs[vi]      = new Vector2(x / (float)(chunkSize - 1),
                                              z / (float)(chunkSize - 1));
                vi++;
            }
        }

        // ── PASS 2: slope + biome colour + vegetation candidates
        //  We need neighbours to estimate slope, so do a second pass.
        vi = 0;
        System.Random rng = new System.Random(
            (int)(position.x * 73856093) ^ (int)(position.y * 19349663));

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                float slope = EstimateSlope(md.vertices, x, z);

                float normH    = md.vertices[vi].y / heightMult;
                float plainMask= Mathf.SmoothStep(0.26f, 0.10f, normH);
                BiomeType biome = BiomeData.Classify(normH, slope, plainMask);
                float snowBlend = Mathf.SmoothStep(0.80f, 1.0f, normH);
                md.colors[vi]  = BiomeData.BiomeColor(biome, snowBlend);

                // Vegetation candidate sampling (jittered grid)
                if (rng.NextDouble() < vegetationDensity
                    && biome != BiomeType.Cliff
                    && biome != BiomeType.Mountain
                    && biome != BiomeType.RiverBank
                    && slope  < 0.35f)
                {
                    Vector3 wp = new Vector3(
                        md.vertices[vi].x + position.x * chunkWorldSize,
                        md.vertices[vi].y,
                        md.vertices[vi].z + position.y * chunkWorldSize);

                    md.spawnPoints.Add(new VegetationPoint
                    {
                        worldPos = wp,
                        normal   = Vector3.up,    // refined later on main thread
                        biome    = biome,
                        slope    = slope
                    });
                }

                vi++;
            }
        }

        // ── PASS 3: triangles ────────────────────────────────
        for (int z = 0; z < chunkSize - 1; z++)
        {
            for (int x = 0; x < chunkSize - 1; x++)
            {
                int v = z * chunkSize + x;
                md.triangles[tris++] = v;
                md.triangles[tris++] = v + chunkSize;
                md.triangles[tris++] = v + 1;
                md.triangles[tris++] = v + 1;
                md.triangles[tris++] = v + chunkSize;
                md.triangles[tris++] = v + chunkSize + 1;
            }
        }

        ResultData = md;
        IsDone     = true;   // volatile write – visible to main thread immediately
    }

    // ── Helpers ──────────────────────────────────────────────
    float ScaleX() => (float)chunkWorldSize / (chunkSize - 1);
    float ScaleZ() => (float)chunkWorldSize / (chunkSize - 1);

    float WorldX(int x) => (x + position.x * (chunkSize - 1)) * ScaleX();
    float WorldZ(int z) => (z + position.y * (chunkSize - 1)) * ScaleZ();

    float SampleH(float wx, float wz) =>
        NoiseUtility.SampleHeight(
            wx, wz, octaves, baseFrequency, persistence, lacunarity,
            mountainBlend, mountainFreq,
            riverFrequency, riverWidth, riverDepth, heightMult,
            bankWidth, bankFlatness,
            valleyFrequency, valleyDepth,
            exponent, offsetX, offsetZ);

    float EstimateSlope(Vector3[] verts, int x, int z)
    {
        // Use central difference on the four cardinal neighbours
        int Idx(int xi, int zi) => Mathf.Clamp(zi, 0, chunkSize-1) * chunkSize
                                 + Mathf.Clamp(xi, 0, chunkSize-1);
        float dx = (verts[Idx(x+1,z)].y - verts[Idx(x-1,z)].y) * 0.5f;
        float dz = (verts[Idx(x,z+1)].y - verts[Idx(x,z-1)].y) * 0.5f;
        float rise = Mathf.Sqrt(dx*dx + dz*dz);
        float run  = ScaleX();   // horizontal distance between samples
        return Mathf.Clamp01(rise / (run + rise + 0.001f));
    }
}
