// ============================================================
//  BiomeData.cs
//  Defines the biome types used per-vertex and exposes a
//  static classifier that returns a 0-1 weight per biome.
//  Vertex colours are packed: R=mountain, G=river bank, B=plain
// ============================================================
using UnityEngine;

public enum BiomeType
{
    PlainGrass,   // flat low areas
    Forest,       // mid elevation, slightly rolling
    RiverBank,    // carved channel + flat banks
    Valley,       // wide low basin between ridges
    Mountain      // high ridges + peaks
}

public static class BiomeData
{
    // ----------------------------------------------------------
    //  Given a normalised height [0..1] and a "flatness" value
    //  [0..1], return which biome this vertex falls in.
    //  These thresholds are tuned for the default settings;
    //  they can be exposed as inspector fields if desired.
    // ----------------------------------------------------------
    public static BiomeType Classify(float normHeight, float flatness)
    {
        if (normHeight > 0.65f) return BiomeType.Mountain;
        if (normHeight > 0.42f) return BiomeType.Forest;
        if (flatness   > 0.70f && normHeight < 0.25f) return BiomeType.Valley;
        if (normHeight < 0.20f) return BiomeType.RiverBank;
        return BiomeType.PlainGrass;
    }

    // ----------------------------------------------------------
    //  Pack biome weights into a Color for the vertex colour
    //  stream so a shader can blend textures.
    //  R = mountain, G = river/bank, B = forest, A = plain
    // ----------------------------------------------------------
    public static Color BiomeColor(BiomeType biome, float blend)
    {
        switch (biome)
        {
            case BiomeType.Mountain:   return new Color(1f,      0f,    0f,    0f);
            case BiomeType.RiverBank:  return new Color(0f,      1f,    0f,    0f);
            case BiomeType.Forest:     return new Color(0f,      0f,    1f,    0f);
            case BiomeType.Valley:     return new Color(0f,      0.5f,  0.5f,  0f);
            default:                   return new Color(0f,      0f,    0f,    1f);
        }
    }
}
