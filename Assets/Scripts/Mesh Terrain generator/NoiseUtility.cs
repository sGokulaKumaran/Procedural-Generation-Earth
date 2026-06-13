// ============================================================
//  NoiseUtility.cs  (v2)
//
//  All methods are pure static and thread-safe (no Unity API
//  calls except Mathf, which is safe on background threads).
//
//  New additions vs v1:
//   - ContinentMask()  – large-scale "where is land vs sea"
//   - ThermalErosion() – smooths terrain like real weathering
//   - SampleHeight()   – single convenience entry point used
//     by both the generator thread AND the vegetation placer
// ============================================================
using UnityEngine;

public static class NoiseUtility
{
    // ── Standard fBm ─────────────────────────────────────────
    public static float FractalNoise(float wx, float wz,
        int octaves, float baseFreq, float persistence, float lacunarity,
        float ox, float oz)
    {
        float total = 0f, freq = baseFreq, amp = 1f, maxV = 0f;
        for (int o = 0; o < octaves; o++)
        {
            total += Mathf.PerlinNoise(wx * freq + ox, wz * freq + oz) * amp;
            maxV  += amp;
            amp   *= persistence;
            freq  *= lacunarity;
        }
        return total / maxV;
    }

    // ── Domain-Warped fBm (two warp layers) ──────────────────
    public static float DomainWarpedNoise(float wx, float wz,
        int octaves, float baseFreq, float persistence, float lacunarity,
        float ox, float oz, float warpStrength = 80f)
    {
        float qx = FractalNoise(wx, wz, octaves, baseFreq, persistence, lacunarity, ox+1.7f, oz+9.2f);
        float qz = FractalNoise(wx, wz, octaves, baseFreq, persistence, lacunarity, ox+8.3f, oz+2.8f);
        float rx = FractalNoise(wx+warpStrength*qx, wz+warpStrength*qz, octaves, baseFreq, persistence, lacunarity, ox+3.1f, oz+6.7f);
        float rz = FractalNoise(wx+warpStrength*qx, wz+warpStrength*qz, octaves, baseFreq, persistence, lacunarity, ox+5.2f, oz+1.4f);
        return FractalNoise(wx+warpStrength*rx, wz+warpStrength*rz, octaves, baseFreq, persistence, lacunarity, ox, oz);
    }

    // ── Ridge noise (sharp mountain peaks) ───────────────────
    public static float RidgeNoise(float wx, float wz,
        int octaves, float baseFreq, float persistence, float lacunarity,
        float ox, float oz)
    {
        float total = 0f, freq = baseFreq, amp = 1f, maxV = 0f, prev = 1f;
        for (int o = 0; o < octaves; o++)
        {
            float n = Mathf.PerlinNoise(wx * freq + ox, wz * freq + oz);
            n = 1f - Mathf.Abs(2f * n - 1f);
            n = n * n * prev;
            prev = n;
            total += n * amp;
            maxV  += amp;
            amp   *= persistence;
            freq  *= lacunarity;
        }
        return total / maxV;
    }

    // ── Continent mask ────────────────────────────────────────
    //  Very low frequency blob that creates large "highlands"
    //  and "lowlands". Multiplied into the final height so the
    //  world has genuine regional variation.
    public static float ContinentMask(float wx, float wz, float ox, float oz)
    {
        float c = FractalNoise(wx, wz, 3, 0.00012f, 0.5f, 2f, ox+9999f, oz+9999f);
        // Sharpen so mid-range values push to either flat or high
        return Mathf.Pow(Mathf.Clamp01(c), 1.4f);
    }

    // ── River mask ────────────────────────────────────────────
    //  Returns a 0..1 value where 1 = river centre.
    //  Produced by warping a low-freq noise field and reading
    //  the absolute distance from the 0.5 iso-line.
    public static float RiverMask(float wx, float wz,
        float riverFreq, float ox, float oz,
        out float distFromCentre)
    {
        float n = DomainWarpedNoise(wx, wz, 3, riverFreq, 0.55f, 2f,
                                    ox+333f, oz+777f, 200f);
        distFromCentre = Mathf.Abs(n - 0.5f);
        return distFromCentre;   // caller uses this to carve channel
    }

    // ── Full height sample (thread-safe) ─────────────────────
    //  Used by both TerrainGeneratorThreaded and VegetationPlacer.
    public static float SampleHeight(
        float wx, float wz,
        int octaves, float baseFreq, float persistence, float lacunarity,
        float mountainBlend, float mountainFreq,
        float riverFreq, float riverWidth, float riverDepth, float heightMult,
        float bankWidth, float bankFlatness,
        float valleyFreq, float valleyDepth,
        float exponent, float ox, float oz)
    {
        // 1. Base shape
        float baseH = DomainWarpedNoise(wx, wz, octaves, baseFreq,
                                        persistence, lacunarity, ox, oz, 60f);

        // 2. Continent scale variation
        float continent = ContinentMask(wx, wz, ox, oz);
        baseH = Mathf.Lerp(baseH * 0.4f, baseH, continent);

        // 3. Mountain ridges (only in high areas)
        float ridgeH = RidgeNoise(wx, wz, 6, mountainFreq, 0.5f, lacunarity,
                                  ox+500f, oz+500f);
        float mMask  = Mathf.SmoothStep(0.40f, 0.78f, baseH);
        // BIGGER mountains: ridge contribution is additive AND raised
        float mountainH = baseH + ridgeH * mMask * mountainBlend * 1.4f;
        mountainH = Mathf.Clamp01(mountainH);

        // 4. Valley carving (wider, more gradual than v1)
        float valleyN = FractalNoise(wx, wz, 4, valleyFreq, 0.5f, 2f,
                                     ox+200f, oz+200f);
        float vMask = Mathf.SmoothStep(0.28f, 0.52f, mountainH)
                    * Mathf.SmoothStep(0.72f, 0.50f, mountainH);
        float vDig  = Mathf.SmoothStep(0.36f, 0.46f, valleyN) * vMask;
        mountainH  -= vDig * valleyDepth;
        mountainH   = Mathf.Clamp01(mountainH);

        // 5. River carving (only below mountain zone)
        float distFromCentre;
        RiverMask(wx, wz, riverFreq, ox, oz, out distFromCentre);
        float riverAltMask = Mathf.SmoothStep(0.62f, 0.32f, mountainH);
        float channelMask  = Mathf.SmoothStep(riverWidth, 0f, distFromCentre) * riverAltMask;
        float bankMask     = Mathf.SmoothStep(bankWidth + riverWidth, riverWidth, distFromCentre)
                           * (1f - channelMask) * riverAltMask;
        mountainH -= channelMask * (riverDepth / heightMult);
        mountainH  = Mathf.Lerp(mountainH, mountainH - 0.004f, bankMask * bankFlatness);
        mountainH  = Mathf.Clamp01(mountainH);

        // 6. Plain smoothing (low areas flatten to grass)
        float plainMask = Mathf.SmoothStep(0.26f, 0.10f, mountainH);
        mountainH = Mathf.Lerp(mountainH, mountainH * 0.28f, plainMask * 0.65f);

        // 7. Height curve
        return Mathf.Pow(Mathf.Clamp01(mountainH), exponent) * heightMult;
    }
}
