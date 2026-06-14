// ============================================================
//  NoiseUtility.cs
//  Static helpers used by both TerrainChunk and RiverGenerator.
//
//  Techniques used:
//   - Fractal Brownian Motion (fBm)  – layered octaves
//   - Domain Warping                 – offsets the sample point
//     with another noise call, creating natural-looking swirls
//     and bends that plain Perlin cannot produce.
//   - Ridge noise                    – inverts the absolute value
//     to create sharp ridges (good for mountain peaks / river banks)
// ============================================================
using UnityEngine;

public static class NoiseUtility
{
    // ----------------------------------------------------------
    //  Standard fBm  (general terrain height)
    // ----------------------------------------------------------
    public static float FractalNoise(
        float wx, float wz,
        int   octaves,
        float baseFreq,
        float persistence,
        float lacunarity,
        float offsetX, float offsetZ)
    {
        float total    = 0f;
        float freq     = baseFreq;
        float amplitude= 1f;
        float maxVal   = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float xc = wx * freq + offsetX;
            float zc = wz * freq + offsetZ;
            total   += Mathf.PerlinNoise(xc, zc) * amplitude;
            maxVal  += amplitude;
            amplitude *= persistence;
            freq      *= lacunarity;
        }
        return total / maxVal;   // normalised [0..1]
    }

    // ----------------------------------------------------------
    //  Domain-Warped fBm
    //  Samples (wx,wz) offset by two separate noise fields.
    //  Produces the organic, swirling shapes seen in real terrain.
    // ----------------------------------------------------------
    public static float DomainWarpedNoise(
        float wx, float wz,
        int   octaves,
        float baseFreq,
        float persistence,
        float lacunarity,
        float offsetX, float offsetZ,
        float warpStrength = 80f)
    {
        // First warp pass: displace the sample point
        float qx = FractalNoise(wx, wz, octaves, baseFreq, persistence, lacunarity,
                                offsetX + 1.7f, offsetZ + 9.2f);
        float qz = FractalNoise(wx, wz, octaves, baseFreq, persistence, lacunarity,
                                offsetX + 8.3f, offsetZ + 2.8f);

        // Second warp pass (optional second layer for extra complexity)
        float rx = FractalNoise(wx + warpStrength * qx, wz + warpStrength * qz,
                                octaves, baseFreq, persistence, lacunarity,
                                offsetX + 3.1f, offsetZ + 6.7f);
        float rz = FractalNoise(wx + warpStrength * qx, wz + warpStrength * qz,
                                octaves, baseFreq, persistence, lacunarity,
                                offsetX + 5.2f, offsetZ + 1.4f);

        return FractalNoise(wx + warpStrength * rx, wz + warpStrength * rz,
                            octaves, baseFreq, persistence, lacunarity,
                            offsetX, offsetZ);
    }

    // ----------------------------------------------------------
    //  Ridge noise  (mountain ridges / valley carving)
    //  f = 1 - |2*noise - 1|  →  spiky peaks
    // ----------------------------------------------------------
    public static float RidgeNoise(
        float wx, float wz,
        int   octaves,
        float baseFreq,
        float persistence,
        float lacunarity,
        float offsetX, float offsetZ)
    {
        float total    = 0f;
        float freq     = baseFreq;
        float amplitude= 1f;
        float maxVal   = 0f;
        float prev     = 1f;  // used to "chain" ridges together

        for (int o = 0; o < octaves; o++)
        {
            float xc = wx * freq + offsetX;
            float zc = wz * freq + offsetZ;
            float n  = Mathf.PerlinNoise(xc, zc);
            n = 1f - Mathf.Abs(2f * n - 1f);  // ridge fold
            n = n * n;                          // sharpen
            n *= prev;
            prev = n;

            total  += n * amplitude;
            maxVal += amplitude;
            amplitude *= persistence;
            freq      *= lacunarity;
        }
        return total / maxVal;
    }
}
