using UnityEngine;

public static class NoiseUtility
{
    // Standard fBm  (general terrain height)
    public static float FractalNoise(
        float wx, float wz,
        int octaves,
        float baseFreq,
        float persistence,
        float lacunarity,
        float offsetX, float offsetZ)
    {
        float total = 0f;
        float freq = baseFreq;
        float amplitude = 1f;
        float maxVal = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float xc = wx * freq + offsetX;
            float zc = wz * freq + offsetZ;
            total += Mathf.PerlinNoise(xc, zc) * amplitude;
            maxVal += amplitude;
            amplitude *= persistence;
            freq *= lacunarity;
        }
        return total / maxVal;
    }

    // Domain-Warped fBm  (organic swirls)
    public static float DomainWarpedNoise(
        float wx, float wz,
        int octaves,
        float baseFreq,
        float persistence,
        float lacunarity,
        float offsetX, float offsetZ,
        float warpStrength = 80f)
    {
        float qx = FractalNoise(wx, wz, octaves, baseFreq, persistence, lacunarity,
                                offsetX + 1.7f, offsetZ + 9.2f);
        float qz = FractalNoise(wx, wz, octaves, baseFreq, persistence, lacunarity,
                                offsetX + 8.3f, offsetZ + 2.8f);

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

    // Ridge noise  (mountain ridges)
    public static float RidgeNoise(
        float wx, float wz,
        int octaves,
        float baseFreq,
        float persistence,
        float lacunarity,
        float offsetX, float offsetZ)
    {
        float total = 0f;
        float freq = baseFreq;
        float amplitude = 1f;
        float maxVal = 0f;
        float prev = 1f;

        for (int o = 0; o < octaves; o++)
        {
            float xc = wx * freq + offsetX;
            float zc = wz * freq + offsetZ;
            float n = Mathf.PerlinNoise(xc, zc);
            n = 1f - Mathf.Abs(2f * n - 1f);
            n = n * n;
            n *= prev;
            prev = n;

            total += n * amplitude;
            maxVal += amplitude;
            amplitude *= persistence;
            freq *= lacunarity;
        }
        return total / maxVal;
    }
}
