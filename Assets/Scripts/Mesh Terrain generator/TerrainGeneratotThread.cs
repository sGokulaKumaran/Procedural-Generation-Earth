using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class TerrainGeneratorThreaded
{
    // The "Result" that the background thread will fill
    public MeshData ResultData;

    // The inputs needed to build the mesh
    public Vector2 position;
    public int chunkSize;
    public int chunkWorldSize;
    public float baseFrequency, persistence, lacunarity, heightMult, exponent;
    public float mountainBlend, mountainFreq;
    public float riverFrequency, riverWidth, riverDepth, bankWidth, bankFlatness;
    public float valleyFrequency, valleyDepth;
    public float offsetX, offsetZ;

    public void Generate()
    {
        // This runs on a background thread!
        MeshData meshData = new MeshData(chunkSize);
        int vi = 0;

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                float finalY = CalculateHeight(x, z);
                meshData.vertices[vi] = new Vector3(x, finalY, z);
                meshData.uvs[vi] = new Vector2(x / (float)(chunkSize - 1), z / (float)(chunkSize - 1));
                meshData.colors[vi] = BiomeData.BiomeColor(BiomeData.Classify(finalY / heightMult, 0f), 1f);
                vi++;
            }
        }

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

    private float CalculateHeight(int x, int z)
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
}
