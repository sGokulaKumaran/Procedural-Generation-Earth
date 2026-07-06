using UnityEngine;

public class WorldGeneratorThreaded
{
    public MeshData ResultData;
    public float[] HeightMap;
    public float[] FlatnessMap;

    public int VerticesPerSide;
    public float WorldSize;
    public int Octaves;
    public float BaseFrequency;
    public float Persistence;
    public float Lacunarity;
    public float HeightMult;
    public float Exponent;
    public float MountainBlend;
    public float MountainFreq;
    public float OffsetX, OffsetZ;

    public void Generate()
    {
        int n = VerticesPerSide;
        int vCount = n * n;
        float worldStep = WorldSize / (n - 1);

        MeshData meshData = new MeshData(n);   // ← meshData.curvature now exists
        HeightMap = new float[vCount];
        FlatnessMap = new float[vCount];

        // heights + flatness
        int vi = 0;
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float wx = x * worldStep;
                float wz = z * worldStep;

                HeightMap[vi] = CalculateHeight(wx, wz);
                FlatnessMap[vi] = CalculateFlatness(wx, wz);
                vi++;
            }
        }

        // vertex positions + uvs
        vi = 0;
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float wx = x * worldStep;
                float wz = z * worldStep;

                meshData.vertices[vi] = new Vector3(wx, HeightMap[vi], wz);
                meshData.uvs[vi] = new Vector2((float)x / (n - 1), (float)z / (n - 1));
                vi++;
            }
        }

        // curvature from neighbor height differences
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int idx = z * n + x;
                int xR = Mathf.Min(x + 1, n - 1);
                int zF = Mathf.Min(z + 1, n - 1);
                int xL = Mathf.Max(x - 1, 0);
                int zB = Mathf.Max(z - 1, 0);

                float dx = (HeightMap[z * n + xR] - HeightMap[z * n + xL]) / (worldStep * 2f);
                float dz = (HeightMap[zF * n + x] - HeightMap[zB * n + x]) / (worldStep * 2f);

                Vector3 nrm = new Vector3(-dx, 1f, -dz).normalized;
                meshData.curvature[idx] = 1f - Mathf.Clamp01(nrm.y);
            }
        }

        //triangles ──
        int tris = 0;
        for (int z = 0; z < n - 1; z++)
        {
            for (int x = 0; x < n - 1; x++)
            {
                int v = z * n + x;
                meshData.triangles[tris + 0] = v;
                meshData.triangles[tris + 1] = v + n;
                meshData.triangles[tris + 2] = v + 1;
                meshData.triangles[tris + 3] = v + 1;
                meshData.triangles[tris + 4] = v + n;
                meshData.triangles[tris + 5] = v + n + 1;
                tris += 6;
            }
        }

        ResultData = meshData;
    }

    float CalculateHeight(float wx, float wz)
    {
        float baseH = NoiseUtility.DomainWarpedNoise(
            wx, wz, Octaves, BaseFrequency, Persistence, Lacunarity,
            OffsetX, OffsetZ, 60f);

        float ridgeH = NoiseUtility.RidgeNoise(
            wx, wz, 3, MountainFreq, 0.5f, Lacunarity,
            OffsetX + 500f, OffsetZ + 500f);

        float combinedH = baseH + ridgeH * Mathf.SmoothStep(0.45f, 0.75f, baseH) * MountainBlend;
        combinedH = Mathf.Clamp01(combinedH);
        return Mathf.Pow(combinedH, Exponent) * HeightMult;
    }

    float CalculateFlatness(float wx, float wz)
    {
        float center = NoiseUtility.FractalNoise(
            wx, wz, 3, BaseFrequency * 0.5f, Persistence, Lacunarity, OffsetX, OffsetZ);

        float diff = 0f;
        int samples = 0;

        for (int dz = -2; dz <= 2; dz += 2)
            for (int dx = -2; dx <= 2; dx += 2)
            {
                if (dx == 0 && dz == 0) continue;
                float n = NoiseUtility.FractalNoise(
                    wx + dx * 5, wz + dz * 5, 3, BaseFrequency * 0.5f,
                    Persistence, Lacunarity, OffsetX, OffsetZ);
                diff += Mathf.Abs(n - center);
                samples++;
            }

        return samples > 0 ? 1f - Mathf.Clamp01(diff / samples * 5f) : 0f;
    }
}
