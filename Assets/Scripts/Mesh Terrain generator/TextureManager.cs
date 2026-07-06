using UnityEngine;

[System.Serializable]
public class BiomeTextureSet
{
    [Header("Grass Variants")]
    public Texture2D grassA;          // standard
    public Texture2D grassB;          // patchy
    public Texture2D grassBright;     // sunlit areas

    [Header("Forest")]
    public Texture2D forestA;
    public Texture2D forestB;

    [Header("Rock / Mountain")]
    public Texture2D rockA;
    public Texture2D rockB;
    public Texture2D cliff;           // near-vertical surfaces

    [Header("Snow")]
    public Texture2D snowA;
    public Texture2D snowB;

    [Header("Dirt / Valley")]
    public Texture2D dirtA;
    public Texture2D dirtB;
    public Texture2D mud;             // very flat / low

    [Header("Blend Settings")]
    [Range(0f, 1f)] public float mountainBlendHeight = 0.65f;
    [Range(0f, 1f)] public float snowBlendHeight = 0.85f;
    [Range(0f, 1f)] public float forestBlendHeight = 0.42f;
    [Range(0f, 1f)] public float valleyFlatnessThreshold = 0.70f;
    [Range(0f, 1f)] public float mudFlatnessThreshold = 0.85f;
    [Range(0f, 1f)] public float cliffCurvatureThreshold = 0.55f;
    [Range(0.0001f, 0.02f)] public float variantNoiseScale = 0.0035f;
}

public class TextureManager : MonoBehaviour
{
    public BiomeTextureSet textures = new BiomeTextureSet();

    [Header("Material")]
    public Material terrainMaterial;

    [Header("Tiling")]
    [Range(0.1f, 50f)] public float globalTiling = 1f;
    [Range(0.1f, 50f)] public float macroTiling = 16f;

    // ----------------------------------------------------------
    //  Per-vertex biome + variant weights.
    //  Vertex colour packing (8-bit per channel):
    //    R = mountain/rock weight
    //    G = snow         weight
    //    B = forest       weight
    //    A = grass        weight
    //
    //  Variant weights are passed via UV2/UV3:
    //    UV2.x = dirt   weight       UV2.y = cliff weight
    //    UV3.x = variant id (0..1, picks between texture variants in the shader)
    // ----------------------------------------------------------

    public void ForceRebindShader()
    {
        if (terrainMaterial == null) return;

        Shader urpShader = Shader.Find("Custom/VertexBlendedTerrain_URP");
        if (urpShader == null)
        {
            Debug.LogError("[TextureManager] Shader 'Custom/VertexBlendedTerrain_URP' not found.");
            return;
        }

        if (terrainMaterial.shader != urpShader)
        {
            terrainMaterial.shader = urpShader;
        }
    }

    /// <summary>
    /// Blends biome/texture weights for one chunk's mesh.
    ///
    /// IMPORTANT: this now samples the "variant" macro-pattern noise using the vertex's
    /// TRUE WORLD position (chunkOriginX/Z + local offset), not the raw local grid index.
    /// The old version scaled the local index x/z directly, which — combined with every
    /// chunk covering the same local index range 0..verticesPerSide — meant every chunk got
    /// an identical texture-variant pattern (part of the repeating-tile look). It also called
    /// UnityEngine.Random.value fresh on every invocation, so the pattern reshuffled every
    /// time a chunk regenerated (LOD swap, cache rebuild), causing visible popping. Both are
    /// fixed by using world-space coordinates plus a fixed, deterministic seed offset that's
    /// the same for the whole world and never changes between calls.
    ///
    /// maxHeight replaces a previously hardcoded "120" used to normalize height into 0..1 —
    /// that silently broke biome banding whenever heightMult was changed from the inspector.
    ///
    /// This method only touches plain structs, arrays and Mathf/Color math, so it's safe to
    /// call from a background thread (used by ChunkMeshBuilder during first-time generation).
    /// </summary>
    public void ApplyBiomeBlending(
        MeshData meshData,
        int verticesPerSide,
        float chunkSize,
        float chunkOriginX,
        float chunkOriginZ,
        float[] heightMap,
        float[] flatnessMap,
        float maxHeight,
        float seedOffsetX,
        float seedOffsetZ)
    {
        if (meshData == null || meshData.colors == null) return;
        if (heightMap == null || flatnessMap == null) return;
        if (heightMap.Length != meshData.colors.Length) return;

        float worldStep = chunkSize / (verticesPerSide - 1);
        float safeMaxHeight = Mathf.Max(1f, maxHeight);

        int vi = 0;
        for (int z = 0; z < verticesPerSide; z++)
        {
            for (int x = 0; x < verticesPerSide; x++)
            {
                float h = heightMap[vi];
                float f = flatnessMap[vi];
                float curv = meshData.curvature[vi];
                float h01 = Mathf.Clamp01(h / safeMaxHeight);

                float worldX = chunkOriginX + x * worldStep;
                float worldZ = chunkOriginZ + z * worldStep;

                // macro variant id 0..1 — world-space + fixed seed offset, so it's
                // continuous across chunk borders and stable across regenerations.
                float variant = Mathf.PerlinNoise(
                    (worldX + seedOffsetX) * textures.variantNoiseScale,
                    (worldZ + seedOffsetZ) * textures.variantNoiseScale);

                float wGrass = 0f;
                float wForest = 0f;
                float wRock = 0f;
                float wSnow = 0f;
                float wDirt = 0f;
                float wCliff = 0f;

                // 1. base altitude bands
                if (h01 < textures.forestBlendHeight)
                {
                    wGrass = 1f - Mathf.SmoothStep(0f, textures.forestBlendHeight, h01);
                    wDirt = Mathf.SmoothStep(0f, textures.forestBlendHeight, h01) * 0.4f;
                }
                else if (h01 < textures.mountainBlendHeight)
                {
                    wForest = Mathf.SmoothStep(textures.forestBlendHeight, textures.mountainBlendHeight, h01);
                    wGrass = 1f - wForest;
                }
                else if (h01 < textures.snowBlendHeight)
                {
                    wRock = Mathf.SmoothStep(textures.mountainBlendHeight, textures.snowBlendHeight, h01);
                    wForest = (1f - wRock) * 0.6f;
                    wGrass = (1f - wRock) * 0.4f;
                }
                else
                {
                    wSnow = Mathf.SmoothStep(textures.snowBlendHeight, 1.0f, h01);
                    wRock = 1f - wSnow;
                }

                // 2. flatness -> mud/dirt
                if (f > textures.mudFlatnessThreshold && h01 < 0.20f)
                {
                    wDirt = Mathf.Max(wDirt, (f - textures.mudFlatnessThreshold)
                                            / (1f - textures.mudFlatnessThreshold));
                    wGrass *= 0.3f;
                }
                else if (f > textures.valleyFlatnessThreshold && h01 < 0.30f)
                {
                    wDirt = Mathf.Max(wDirt, 0.5f);
                    wGrass *= 0.5f;
                }

                // 3. curvature -> cliffs override grass/dirt
                if (curv > textures.cliffCurvatureThreshold)
                {
                    float cliffT = Mathf.InverseLerp(
                        textures.cliffCurvatureThreshold, 1f, curv);
                    wCliff = cliffT;
                    wGrass *= (1f - cliffT);
                    wDirt *= (1f - cliffT);
                    wForest *= (1f - cliffT * 0.5f);
                }

                // 4. normalize
                float total = wGrass + wForest + wRock + wSnow + wDirt + wCliff;
                if (total > 0f)
                {
                    wGrass /= total;
                    wForest /= total;
                    wRock /= total;
                    wSnow /= total;
                    wDirt /= total;
                    wCliff /= total;
                }

                meshData.colors[vi] = new Color(wRock, wSnow, wForest, wGrass);
                meshData.uv2[vi] = new Vector2(wDirt, wCliff);
                meshData.uv3[vi] = new Vector2(variant, 0f);

                vi++;
            }
        }
    }

    public void ConfigureMaterial()
    {
        if (terrainMaterial == null) return;

        SetTex("_GrassTex", textures.grassA);
        SetTex("_GrassTex2", textures.grassB);
        SetTex("_GrassBright", textures.grassBright);
        SetTex("_ForestTex", textures.forestA);
        SetTex("_ForestTex2", textures.forestB);
        SetTex("_RockTex", textures.rockA);
        SetTex("_RockTex2", textures.rockB);
        SetTex("_CliffTex", textures.cliff);
        SetTex("_SnowTex", textures.snowA);
        SetTex("_SnowTex2", textures.snowB);
        SetTex("_DirtTex", textures.dirtA);
        SetTex("_DirtTex2", textures.dirtB);
        SetTex("_MudTex", textures.mud);

        terrainMaterial.SetFloat("_GlobalTiling", globalTiling);
        terrainMaterial.SetFloat("_MacroTiling", macroTiling);
    }

    void SetTex(string name, Texture2D tex)
    {
        if (tex != null) terrainMaterial.SetTexture(name, tex);
    }
}