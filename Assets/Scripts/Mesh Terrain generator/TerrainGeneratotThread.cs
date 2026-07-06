using UnityEngine;

/// <summary>
/// Produces the CANONICAL terrain data for one chunk coordinate, always at full
/// resolution, always sampled at true world-space coordinates.
///
/// This replaces the old WorldGeneratorThreaded / TerrainGeneratotThread.cs, which sampled
/// every chunk using LOCAL coordinates (0..chunkSize) and a constant global offset — meaning
/// every chunk produced byte-for-byte identical noise. That was the cause of the repeating
/// "checkerboard tile" look. Here, ChunkOriginX/Z (the chunk's true world position) is added
/// to every sample BEFORE it goes into any noise function, so neighboring chunks sample a
/// truly continuous field and tile seamlessly.
///
/// Height is built from three separate, independent algorithms — plains, mountains, valleys —
/// blended together by a slow-moving "region mask" noise field so mountain ranges and valleys
/// form coherent, clustered regions instead of appearing wherever local noise happens to spike.
/// Slope/steepness is NOT a separate algorithm — it's a derived measurement (the gradient of
/// the final height field), used downstream to drive cliff texturing.
///
/// Normals and "flatness" are computed from a PADDED sample grid (extra ring(s) of samples
/// outside the chunk's own bounds). Because the underlying noise is a pure function of world
/// position, the padding samples exactly the same values a neighboring chunk would compute at
/// that shared border — so both chunks derive identical normals along the seam. This removes
/// the lighting-seam and cliff-texture-seam artifacts caused by RecalculateNormals() only
/// knowing about triangles inside a single chunk.
/// </summary>
public class ChunkTerrainGenerator
{
    // ── chunk placement ──
    public float ChunkOriginX;
    public float ChunkOriginZ;
    public float ChunkSize;
    public int VerticesPerSide;

    // ── world seed (one fixed pair for the whole world, NOT per chunk) ──
    public float SeedOffsetX;
    public float SeedOffsetZ;

    // ── base shape ──
    public int Octaves;
    public float BaseFrequency;
    public float Persistence;
    public float Lacunarity;
    public float HeightMult;
    public float Exponent;

    // ── mountains ──
    public float MountainFreq;
    public float MountainBlend;

    // ── region control (decides WHERE mountains / plains / valleys sit) ──
    public float RegionFrequencyMultiplier = 0.08f;
    public float MountainRegionThreshold = 0.55f;
    public float MountainRegionSharpness = 0.74f;
    public float ValleyRegionThreshold = 0.32f;
    public float ValleyRegionSharpness = 0.14f;
    public float ValleyDepth = 0.16f;
    public float ContinentInfluence = 0.30f;

    // ── outputs (all length VerticesPerSide * VerticesPerSide) ──
    public float[] HeightMap;
    public float[] FlatnessMap;
    public Vector3[] Normals;
    public float[] Curvature;

    const int Pad = 2; // extra ring of samples on every side, needed for seam-correct gradients

    public void Generate()
    {
        int n = VerticesPerSide;
        float step = ChunkSize / (n - 1);
        int pn = n + Pad * 2;

        float[] padded = new float[pn * pn];

        for (int pz = 0; pz < pn; pz++)
        {
            for (int px = 0; px < pn; px++)
            {
                float worldX = ChunkOriginX + (px - Pad) * step;
                float worldZ = ChunkOriginZ + (pz - Pad) * step;
                padded[pz * pn + px] = SampleHeight01(worldX, worldZ) * HeightMult;
            }
        }

        HeightMap = new float[n * n];
        FlatnessMap = new float[n * n];
        Normals = new Vector3[n * n];
        Curvature = new float[n * n];

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int px = x + Pad;
                int pz = z + Pad;
                int pIdx = pz * pn + px;
                int idx = z * n + x;

                float h = padded[pIdx];
                HeightMap[idx] = h;

                // fine gradient (±1 sample) -> normal + curvature (cliff driver)
                float hL1 = padded[pz * pn + (px - 1)];
                float hR1 = padded[pz * pn + (px + 1)];
                float hD1 = padded[(pz - 1) * pn + px];
                float hU1 = padded[(pz + 1) * pn + px];
                float dx1 = (hR1 - hL1) / (2f * step);
                float dz1 = (hU1 - hD1) / (2f * step);

                Vector3 normal = new Vector3(-dx1, 1f, -dz1).normalized;
                Normals[idx] = normal;
                Curvature[idx] = 1f - Mathf.Clamp01(normal.y);

                // broad gradient (±2 samples) -> flatness (valley/mud driver).
                // Derived from the REAL heightmap now, not a disconnected noise sample
                // like the original CalculateFlatness — so mud/valley placement now
                // actually corresponds to where the terrain is flat.
                float hL2 = padded[pz * pn + (px - 2)];
                float hR2 = padded[pz * pn + (px + 2)];
                float hD2 = padded[(pz - 2) * pn + px];
                float hU2 = padded[(pz + 2) * pn + px];
                float dx2 = (hR2 - hL2) / (4f * step);
                float dz2 = (hU2 - hD2) / (4f * step);
                float slope2 = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2);
                FlatnessMap[idx] = 1f - Mathf.Clamp01(slope2 * 4f);
            }
        }
    }

    float SampleHeight01(float wx, float wz)
    {
        float sx = wx + SeedOffsetX;
        float sz = wz + SeedOffsetZ;

        // 1. Continent shape — very large scale landmass bias
        float continent = NoiseUtility.FractalNoise(
            sx, sz, 2, BaseFrequency * 0.15f, Persistence, Lacunarity, 1000f, 1000f);

        // 2. Region mask — large, slow-moving field that decides WHERE mountain
        //    ranges / valleys / plains sit, independent of local height noise.
        float region = NoiseUtility.FractalNoise(
            sx, sz, 3, BaseFrequency * RegionFrequencyMultiplier, 0.5f, Lacunarity, 4000f, 4000f);

        float mountainWeight = Mathf.SmoothStep(MountainRegionThreshold, MountainRegionSharpness, region);
        float valleyWeight = Mathf.SmoothStep(ValleyRegionThreshold, ValleyRegionSharpness, region);

        // 3. Plains algorithm — gentle rolling hills, organic domain-warped fBm
        float plains = NoiseUtility.DomainWarpedNoise(
            sx, sz, Octaves, BaseFrequency, Persistence, Lacunarity, 0f, 0f, 40f);

        // 4. Mountain algorithm — ridged multifractal peaks on a warped foothill base
        float mountainBase = NoiseUtility.DomainWarpedNoise(
            sx, sz, Octaves, MountainFreq * 0.5f, Persistence, Lacunarity, 2500f, 2500f, 50f);
        float ridges = NoiseUtility.RidgeNoise(
            sx, sz, 4, MountainFreq, 0.5f, Lacunarity, 500f, 500f);
        float mountain = Mathf.Lerp(mountainBase, ridges, 0.7f);
        mountain = Mathf.Pow(Mathf.Clamp01(mountain), Exponent);

        // 5. Valley algorithm — winding low channels carved through plains/foothills
        float valleyField = NoiseUtility.FractalNoise(
            sx, sz, 3, BaseFrequency * 0.3f, 0.5f, Lacunarity, 7000f, 7000f);
        float valleyCarve = 1f - Mathf.Abs(valleyField - 0.5f) * 2f; // ridge-shaped channel mask
        valleyCarve = Mathf.Pow(Mathf.Clamp01(valleyCarve), 3f) * valleyWeight;

        // 6. Blend plains vs mountain by region weight, then carve valleys in
        float terrain = Mathf.Lerp(plains, mountain, mountainWeight * MountainBlend);
        terrain -= valleyCarve * ValleyDepth;

        // 7. Continental bias on top
        float combined = continent * ContinentInfluence + terrain * (1f - ContinentInfluence);
        return Mathf.Clamp01(combined);
    }
}