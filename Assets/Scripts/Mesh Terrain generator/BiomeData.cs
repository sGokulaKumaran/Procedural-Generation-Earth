// ============================================================
//  BiomeData.cs  (v2)
//  Biome classification + vertex-colour packing.
//
//  Vertex colour channels (read by the shader):
//    R = Mountain rock / snow
//    G = River bank / wet sand
//    B = Forest floor / moss
//    A = Plain grass
//
//  NEW: slope is now factored in so steep cliff faces always
//  get rock colour regardless of elevation.
// ============================================================
using UnityEngine;

public enum BiomeType
{
    PlainGrass,
    Forest,
    RiverBank,
    Valley,
    Mountain,
    Cliff       // steep slope on any biome → rock face
}

public static class BiomeData
{
    // normHeight [0..1], slope [0..1] (0=flat, 1=vertical), plainMask [0..1]
    public static BiomeType Classify(float normHeight, float slope, float plainMask)
    {
        if (slope > 0.55f)              return BiomeType.Cliff;
        if (normHeight > 0.72f)         return BiomeType.Mountain;
        if (normHeight > 0.42f)         return BiomeType.Forest;
        if (plainMask  > 0.70f
            && normHeight < 0.25f)      return BiomeType.Valley;
        if (normHeight < 0.18f)         return BiomeType.RiverBank;
        return BiomeType.PlainGrass;
    }

    // Pack into vertex colour for shader blend
    public static Color BiomeColor(BiomeType b, float snowBlend)
    {
        switch (b)
        {
            case BiomeType.Mountain:
                // fade to white (snow) at the very top
                return Color.Lerp(new Color(0.7f, 0f, 0f, 0f),
                                  new Color(1f,   0f, 0f, 0f), snowBlend);
            case BiomeType.Cliff:
                return new Color(0.85f, 0f, 0.1f, 0.05f);   // mostly rock
            case BiomeType.RiverBank:
                return new Color(0f, 1f, 0f, 0f);
            case BiomeType.Forest:
                return new Color(0f, 0f, 1f, 0f);
            case BiomeType.Valley:
                return new Color(0f, 0.3f, 0.4f, 0.3f);
            default:
                return new Color(0f, 0f, 0.1f, 0.9f);        // plain grass
        }
    }
}
