// ============================================================
//  TextureManager.cs
//  Assigns and blends textures based on biome.
//  Uses vertex colors (passed via MeshData.colors) to determine
//  which texture to use per vertex.
//
//  Channel mapping:
//    R = mountain blend weight
//    G = river bank blend weight
//    B = forest blend weight
//    A = river/water blend weight (0 = dry, 1 = water surface)
//
//  The shader uses these channels to blend between textures.
// ============================================================
using UnityEngine;

[System.Serializable]
public class BiomeTextureSet
{
    [Header("Textures")]
    public Texture2D grassTexture;
    public Texture2D forestTexture;
    public Texture2D mountainTexture;
    public Texture2D riverBankTexture;
    public Texture2D waterTexture;
    public Texture2D valleyTexture;

    [Header("Blend Settings")]
    [Range(0f, 1f)] public float mountainBlendHeight = 0.65f;
    [Range(0f, 1f)] public float forestBlendHeight   = 0.42f;
    [Range(0f, 1f)] public float riverBankThreshold  = 0.20f;
    [Range(0f, 1f)] public float waterThreshold      = 0.05f;
}

public class TextureManager : MonoBehaviour
{
    [Header("Biome Textures")]
    public BiomeTextureSet textures = new BiomeTextureSet();

    [Header("Material")]
    public Material terrainMaterial;

    [Header("Tiling")]
    [Range(0.1f, 50f)] public float globalTiling = 1f;

    // ----------------------------------------------------------
    //  Build a tri-planar shader-friendly color array for the mesh
    //  This is what gets passed to MeshData.colors[]
    // ----------------------------------------------------------
    public void ApplyBiomeBlending(
        MeshData meshData,
        int chunkSize,
        Vector2 worldOrigin,
        float[] heightMap,
        float[] riverMap,
        float[] flatnessMap)
    {
        if (meshData.colors == null || meshData.colors.Length == 0) return;

        int vi = 0;
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                float h   = heightMap[vi];
                float r   = riverMap[vi];
                float f   = flatnessMap[vi];
                float h01 = Mathf.Clamp01(h / 120f);

                // Default weights
                float wMountain  = 0f;
                float wForest    = 0f;
                float wGrass     = 1f;
                float wRiverBank = 0f;
                float wWater     = 0f;
                float wValley    = 0f;

                // Height-based biome classification
                if (h01 > textures.mountainBlendHeight)
                {
                    wMountain = Mathf.SmoothStep(textures.mountainBlendHeight, 0.85f, h01);
                    wGrass   *= (1f - wMountain);
                }
                else if (h01 > textures.forestBlendHeight)
                {
                    wForest = Mathf.SmoothStep(textures.forestBlendHeight, textures.mountainBlendHeight, h01);
                    wGrass *= (1f - wForest * 0.7f);
                }

                // Flatness-based valley
                if (f > 0.7f && h01 < 0.3f)
                {
                    wValley = (f - 0.7f) / 0.3f;
                    wGrass *= (1f - wValley * 0.5f);
                }

                // River/water masking
                if (r > textures.waterThreshold)
                {
                    // Deep water
                    float waterDepth = Mathf.InverseLerp(textures.waterThreshold, 5f, r);
                    wWater = waterDepth;
                    wGrass = 0f;
                    wMountain = 0f;
                    wForest = 0f;
                    wValley = 0f;
                }
                else if (r > 0f)
                {
                    // River bank
                    wRiverBank = 1f - (r / textures.waterThreshold);
                    wRiverBank = Mathf.Clamp01(wRiverBank);
                    wGrass *= 0.3f; // dampen grass near rivers
                }

                // Normalize weights
                float total = wMountain + wForest + wGrass + wRiverBank + wWater + wValley;
                if (total > 0f)
                {
                    wMountain  /= total;
                    wForest    /= total;
                    wGrass     /= total;
                    wRiverBank /= total;
                    wWater     /= total;
                    wValley    /= total;
                }

                // Pack into color
                meshData.colors[vi] = new Color(
                    wMountain,                  // R
                    wRiverBank,                 // G
                    wForest,                    // B
                    wWater + wValley * 0.5f     // A
                );

                vi++;
            }
        }
    }

    // ----------------------------------------------------------
    //  Configure the terrain material with all biome textures
    //  Call this once at startup
    // ----------------------------------------------------------
    public void ConfigureMaterial()
    {
        if (terrainMaterial == null) return;

        // Use shader property names that match a custom tri-planar shader
        if (textures.grassTexture != null)
            terrainMaterial.SetTexture("_GrassTex", textures.grassTexture);
        if (textures.forestTexture != null)
            terrainMaterial.SetTexture("_ForestTex", textures.forestTexture);
        if (textures.mountainTexture != null)
            terrainMaterial.SetTexture("_MountainTex", textures.mountainTexture);
        if (textures.riverBankTexture != null)
            terrainMaterial.SetTexture("_RiverBankTex", textures.riverBankTexture);
        if (textures.waterTexture != null)
            terrainMaterial.SetTexture("_WaterTex", textures.waterTexture);
        if (textures.valleyTexture != null)
            terrainMaterial.SetTexture("_ValleyTex", textures.valleyTexture);

        terrainMaterial.SetFloat("_GlobalTiling", globalTiling);
    }
}
