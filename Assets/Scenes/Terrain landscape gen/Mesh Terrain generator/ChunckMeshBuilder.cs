using UnityEngine;

/// <summary>
/// Turns canonical per-chunk data (always sampled at full resolution by
/// ChunkTerrainGenerator) into an actual MeshData at whatever LOD vertex count is
/// currently needed, by bilinear resampling.
///
/// This is the piece that makes LOD tier changes cheap: once a chunk's canonical data
/// exists in the cache, building a mesh for ANY tier (near/mid/far) is pure array math —
/// no noise evaluation, no background thread required. It also means every tier is always
/// resampled from the SAME best-quality source, so there's no "upgrade from a blurry cache"
/// problem the original BuildFromCache had.
///
/// Biome blending happens here too (instead of inside Chunk.ApplyMesh) so the whole mesh —
/// geometry, normals, and vertex-painted textures — is fully prepared before it ever touches
/// the main thread. All the math used here (Mathf.*, Color, Vector3) is safe to call from a
/// background thread; only the final Unity Mesh object creation needs the main thread.
/// </summary>
public static class ChunkMeshBuilder
{
    public static MeshData Build(
        int targetVerts,
        float chunkSize,
        float chunkOriginX,
        float chunkOriginZ,
        float[] srcHeight,
        float[] srcFlatness,
        Vector3[] srcNormal,
        int srcVerts,
        TextureManager textureManager,
        float maxHeight,
        float seedOffsetX,
        float seedOffsetZ)
    {
        int n = targetVerts;
        var data = new MeshData(n);
        float worldStep = chunkSize / (n - 1);

        float[] sampledHeight = new float[n * n];
        float[] sampledFlatness = new float[n * n];

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int vi = z * n + x;
                float localX = x * worldStep;
                float localZ = z * worldStep;

                float h = BilinearSampleFloat(srcHeight, srcVerts, chunkSize, localX, localZ);
                Vector3 nrm = BilinearSampleNormal(srcNormal, srcVerts, chunkSize, localX, localZ);
                float flat = BilinearSampleFloat(srcFlatness, srcVerts, chunkSize, localX, localZ);

                data.vertices[vi] = new Vector3(localX, h, localZ);
                data.normals[vi] = nrm;
                data.uvs[vi] = new Vector2((float)x / (n - 1), (float)z / (n - 1));
                data.curvature[vi] = 1f - Mathf.Clamp01(nrm.y);

                sampledHeight[vi] = h;
                sampledFlatness[vi] = flat;
            }
        }

        BuildTriangles(data, n);

        if (textureManager != null)
        {
            textureManager.ApplyBiomeBlending(
                data, n, chunkSize, chunkOriginX, chunkOriginZ,
                sampledHeight, sampledFlatness, maxHeight, seedOffsetX, seedOffsetZ);
        }

        return data;
    }

    static void BuildTriangles(MeshData data, int n)
    {
        int tris = 0;
        for (int z = 0; z < n - 1; z++)
        {
            for (int x = 0; x < n - 1; x++)
            {
                int v = z * n + x;
                data.triangles[tris + 0] = v;
                data.triangles[tris + 1] = v + n;
                data.triangles[tris + 2] = v + 1;
                data.triangles[tris + 3] = v + 1;
                data.triangles[tris + 4] = v + n;
                data.triangles[tris + 5] = v + n + 1;
                tris += 6;
            }
        }
    }

    static void BilinearCoords(int srcN, float srcWorldSize, float wx, float wz,
        out int x0, out int z0, out float tx, out float tz)
    {
        float u = Mathf.Clamp01(wx / srcWorldSize);
        float v = Mathf.Clamp01(wz / srcWorldSize);
        float fx = u * (srcN - 1);
        float fz = v * (srcN - 1);
        x0 = Mathf.Clamp((int)fx, 0, srcN - 2);
        z0 = Mathf.Clamp((int)fz, 0, srcN - 2);
        tx = fx - x0;
        tz = fz - z0;
    }

    public static float BilinearSampleFloat(float[] src, int srcN, float srcWorldSize, float wx, float wz)
    {
        BilinearCoords(srcN, srcWorldSize, wx, wz, out int x0, out int z0, out float tx, out float tz);

        float h00 = src[z0 * srcN + x0];
        float h10 = src[z0 * srcN + x0 + 1];
        float h01 = src[(z0 + 1) * srcN + x0];
        float h11 = src[(z0 + 1) * srcN + x0 + 1];

        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    public static Vector3 BilinearSampleNormal(Vector3[] src, int srcN, float srcWorldSize, float wx, float wz)
    {
        BilinearCoords(srcN, srcWorldSize, wx, wz, out int x0, out int z0, out float tx, out float tz);

        Vector3 n00 = src[z0 * srcN + x0];
        Vector3 n10 = src[z0 * srcN + x0 + 1];
        Vector3 n01 = src[(z0 + 1) * srcN + x0];
        Vector3 n11 = src[(z0 + 1) * srcN + x0 + 1];

        Vector3 result = Vector3.Lerp(Vector3.Lerp(n00, n10, tx), Vector3.Lerp(n01, n11, tx), tz);
        return result.sqrMagnitude > 0.0001f ? result.normalized : Vector3.up;
    }
}