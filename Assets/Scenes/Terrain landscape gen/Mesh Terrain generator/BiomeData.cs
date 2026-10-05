using UnityEngine;

public enum BiomeType
{
    Grass,
    Forest,
    Mountain,
    Valley
}

public static class BiomeData
{
    public static BiomeType Classify(float normHeight, float flatness)
    {
        if (normHeight > 0.65f) return BiomeType.Mountain;
        if (normHeight > 0.42f) return BiomeType.Forest;
        if (flatness > 0.70f && normHeight < 0.25f) return BiomeType.Valley;
        return BiomeType.Grass;
    }

    /// <summary>
    /// Curvature 0 = flat land, 1 = vertical cliff.
    /// Used to drive rock vs grass blending in the shader.
    /// </summary>
    public static float CurvatureFromNormal(float ny)
    {
        // ny = 1  -> horizontal
        // ny = 0  -> vertical
        return 1f - Mathf.Clamp01(ny);
    }
}
