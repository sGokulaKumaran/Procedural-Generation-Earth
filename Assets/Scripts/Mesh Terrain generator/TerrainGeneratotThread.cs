using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class TerrainGeneratorThreaded
{
    public MeshData ResultData;
    public float[] heightMap;
    public float[] riverMask;       // 0 = no river, 1 = full river
    public float[] flatnessMap;

    public Vector2 position;
    public int chunkSize;
    public int chunkWorldSize;
    public float baseFrequency, persistence, lacunarity, heightMult, exponent;
    public float mountainBlend, mountainFreq;
    public float riverFrequency, riverWidth, riverDepth, bankWidth, bankFlatness;
    public float valleyFrequency, valleyDepth;
    public float offsetX, offsetZ;
    public bool useRivers = true;
    public int seed = 42;

    public void Generate()
    {
        MeshData meshData = new MeshData(chunkSize);
        int vertCount = chunkSize * chunkSize;

        heightMap = new float[vertCount];
        riverMask = new float[vertCount];  // 0 = dry, 1 = river
        flatnessMap = new float[vertCount];

        // ── Step 1: Generate base heights ──
        int vi = 0;
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                heightMap[vi] = CalculateHeight(x, z);
                flatnessMap[vi] = CalculateFlatness(x, z);
                vi++;
            }
        }

        // ── Step 2: Generate river paths (does NOT carve height) ──
        if (useRivers)
        {
            GenerateRiverMask();
        }

        // ── Step 3: Build mesh ──
        vi = 0;
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                float finalY = heightMap[vi];
                meshData.vertices[vi] = new Vector3(x, finalY, z);
                meshData.uvs[vi] = new Vector2(
                    x / (float)(chunkSize - 1),
                    z / (float)(chunkSize - 1));
                meshData.colors[vi] = PackBiomeColor(
                    heightMap[vi],
                    riverMask[vi],
                    flatnessMap[vi]);
                vi++;
            }
        }

        // ── Step 4: Build triangles ──
        int tris = 0;
        for (int z = 0; z < chunkSize - 1; z++)
        {
            for (int x = 0; x < chunkSize - 1; x++)
            {
                int v = z * chunkSize + x;
                meshData.triangles[tris + 0] = v;
                meshData.triangles[tris + 1] = v + chunkSize;
                meshData.triangles[tris + 2] = v + 1;
                meshData.triangles[tris + 3] = v + 1;
                meshData.triangles[tris + 4] = v + chunkSize;
                meshData.triangles[tris + 5] = v + chunkSize + 1;
                tris += 6;
            }
        }

        ResultData = meshData;
    }

    // ----------------------------------------------------------
    //  River Mask Generation (does NOT carve height)
    //  Creates a mask of which vertices are rivers/banks
    // ----------------------------------------------------------
    void GenerateRiverMask()
    {
        System.Random rng = new System.Random(seed);

        // Find the MAX height in this chunk
        float maxHeight = 0f;
        for (int i = 0; i < heightMap.Length; i++)
            if (heightMap[i] > maxHeight) maxHeight = heightMap[i];

        // Only spawn rivers if the chunk has significant mountains
        float peakThreshold = maxHeight * 0.65f;
        if (peakThreshold < 35f) return;

        // Find peaks (local maxima above threshold)
        List<Vector2Int> peaks = new List<Vector2Int>();
        for (int z = 5; z < chunkSize - 5; z += 6)
        {
            for (int x = 5; x < chunkSize - 5; x += 6)
            {
                int idx = z * chunkSize + x;
                if (heightMap[idx] < peakThreshold) continue;

                // Check if local maximum
                bool isLocalMax = true;
                int checkRadius = 4;
                for (int dz = -checkRadius; dz <= checkRadius && isLocalMax; dz++)
                {
                    for (int dx = -checkRadius; dx <= checkRadius && isLocalMax; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx;
                        int nz = z + dz;
                        if (nx < 0 || nx >= chunkSize || nz < 0 || nz >= chunkSize) continue;
                        int nIdx = nz * chunkSize + nx;
                        if (heightMap[nIdx] > heightMap[idx]) isLocalMax = false;
                    }
                }

                if (isLocalMax) peaks.Add(new Vector2Int(x, z));
            }
        }

        // Limit to 1-2 rivers per chunk
        int maxRivers = Mathf.Min(peaks.Count, 1 + rng.Next(2));
        for (int i = 0; i < maxRivers; i++)
        {
            if (peaks.Count == 0) break;
            Vector2Int peak = peaks[rng.Next(peaks.Count)];
            peaks.Remove(peak); // Don't pick same peak twice
            TraceRiverMask(peak.x, peak.y, rng);
        }
    }

    void TraceRiverMask(int startX, int startZ, System.Random rng)
    {
        int x = startX;
        int z = startZ;
        int maxSteps = chunkSize * 4;

        // River width (small, ~2-3 vertices wide)
        int riverWidth = 2;
        // Bank width (extends 2-3 cells beyond river)
        int bankWidth = 4;

        for (int step = 0; step < maxSteps; step++)
        {
            // Mark river center (full mask = 1.0)
            int idx = z * chunkSize + x;
            riverMask[idx] = Mathf.Max(riverMask[idx], 1.0f);

            // Mark river width
            for (int dz = -riverWidth; dz <= riverWidth; dz++)
            {
                for (int dx = -riverWidth; dx <= riverWidth; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= chunkSize || nz < 0 || nz >= chunkSize) continue;

                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > riverWidth) continue;

                    float falloff = 1.0f - (dist / (riverWidth + 0.5f));
                    int nIdx = nz * chunkSize + nx;
                    riverMask[nIdx] = Mathf.Max(riverMask[nIdx], falloff);
                }
            }

            // Mark bank zone (partial mask for blending)
            for (int dz = -bankWidth; dz <= bankWidth; dz++)
            {
                for (int dx = -bankWidth; dx <= bankWidth; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= chunkSize || nz < 0 || nz >= chunkSize) continue;

                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist <= riverWidth || dist > bankWidth) continue;

                    // Bank mask: 0.5 at river edge, fading to 0 at bank edge
                    float bankFalloff = 0.5f * (1.0f - (dist - riverWidth) / (bankWidth - riverWidth));
                    int nIdx = nz * chunkSize + nx;
                    riverMask[nIdx] = Mathf.Max(riverMask[nIdx], bankFalloff);
                }
            }

            // Find lowest neighbor with noise for natural curves
            int lowestX = x, lowestZ = z;
            float lowestH = heightMap[idx];

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= chunkSize || nz < 0 || nz >= chunkSize) continue;

                    int nIdx = nz * chunkSize + nx;
                    float noisePush = (Mathf.PerlinNoise(
                        nx * 0.05f + seed * 0.001f,
                        nz * 0.05f + seed * 0.001f) - 0.5f) * 3f;

                    float effectiveH = heightMap[nIdx] + noisePush;
                    if (effectiveH < lowestH)
                    {
                        lowestH = effectiveH;
                        lowestX = nx;
                        lowestZ = nz;
                    }
                }
            }

            // End conditions
            if (lowestX == x && lowestZ == z) break;        // Local minimum
            if (heightMap[lowestZ * chunkSize + lowestX] <= 1f) break;  // Reached ground

            x = lowestX;
            z = lowestZ;
        }
    }

    // ----------------------------------------------------------
    //  Pack biome info into vertex color
    //  NEW packing:
    //    R = mountain weight
    //    G = river-bank weight  
    //    B = forest weight
    //    A = if > 0.5, water mask (0.5=edge, 1.0=center)
    //        if < 0.5, grass weight (0=no grass, 0.5=full grass)
    // ----------------------------------------------------------
    Color PackBiomeColor(float height, float river, float flatness)
    {
        float h01 = Mathf.Clamp01(height / heightMult);

        float wMountain = 0f, wForest = 0f, wGrass = 1f, wRiverBank = 0f;
        float wWater = 0f;

        // Height-based biome classification
        if (h01 > 0.65f)
        {
            wMountain = Mathf.SmoothStep(0.65f, 0.85f, h01);
            wGrass *= (1f - wMountain);
        }
        else if (h01 > 0.42f)
        {
            wForest = Mathf.SmoothStep(0.42f, 0.65f, h01);
            wGrass *= (1f - wForest * 0.7f);
        }

        // River mask: 0.5-1.0 means water, 0-0.5 means bank/dry
        if (river > 0.5f)
        {
            // River center → water
            wWater = (river - 0.5f) * 2f;  // 0-1
            wGrass = 0f;
            wMountain = 0f;
            wForest = 0f;
            wRiverBank = 0f;
        }
        else if (river > 0f)
        {
            // River bank
            wRiverBank = river * 2f;  // 0-1
            wGrass *= 0.4f;  // Dampen grass near banks
        }

        return new Color(wMountain, wRiverBank, wForest, wWater);
    }

    float CalculateHeight(int x, int z)
    {
        float wx = x + position.x * (chunkSize - 1);
        float wz = z + position.y * (chunkSize - 1);
        wx *= (float)chunkWorldSize / (chunkSize - 1);
        wz *= (float)chunkWorldSize / (chunkSize - 1);

        float baseH = NoiseUtility.DomainWarpedNoise(
            wx, wz, 4, baseFrequency, persistence, lacunarity, offsetX, offsetZ, 60f);
        float ridgeH = NoiseUtility.RidgeNoise(
            wx, wz, 3, mountainFreq, 0.5f, lacunarity, offsetX + 500f, offsetZ + 500f);

        float combinedH = baseH + ridgeH * Mathf.SmoothStep(0.45f, 0.75f, baseH) * mountainBlend;
        combinedH = Mathf.Clamp01(combinedH);
        return Mathf.Pow(combinedH, exponent) * heightMult;
    }

    float CalculateFlatness(int x, int z)
    {
        float wx = x + position.x * (chunkSize - 1);
        float wz = z + position.y * (chunkSize - 1);
        wx *= (float)chunkWorldSize / (chunkSize - 1);
        wz *= (float)chunkWorldSize / (chunkSize - 1);

        float center = NoiseUtility.FractalNoise(
            wx, wz, 3, baseFrequency * 0.5f, persistence, lacunarity, offsetX, offsetZ);
        float diff = 0f;
        int samples = 0;

        for (int dz = -2; dz <= 2; dz += 2)
            for (int dx = -2; dx <= 2; dx += 2)
            {
                if (dx == 0 && dz == 0) continue;
                float nx = wx + dx * 5;
                float nz = wz + dz * 5;
                float n = NoiseUtility.FractalNoise(
                    nx, nz, 3, baseFrequency * 0.5f, persistence, lacunarity, offsetX, offsetZ);
                diff += Mathf.Abs(n - center);
                samples++;
            }

        return samples > 0 ? 1f - Mathf.Clamp01(diff / samples * 5f) : 0f;
    }
}
