// ============================================================
//  RiverGenerator.cs
//  Generates river networks that flow from mountains to lowlands.
//  Uses pathfinding-like approach: water flows downhill from peaks
//  until it reaches ground level (y=0).
//
//  Output: Per-vertex river mask stored in MeshData.colors.a
//  0 = no river, 1 = full river
// ============================================================
using UnityEngine;

public static class RiverGenerator
{
    public struct RiverSettings
    {
        public float frequency;       // How often rivers spawn
        public float width;           // River width in world units
        public float depth;           // How deep rivers carve
        public float bankWidth;       // Flat bank zone width
        public float bankFlatness;    // 0=steep, 1=flat
        public int   seed;
    }

    // ----------------------------------------------------------
    //  Main entry: Carve rivers into existing height data
    //  Returns a 2D array of river depths (0 = no river)
    // ----------------------------------------------------------
    public static float[,] CarveRivers(
        float[,] heightMap,
        int width,
        int height,
        Vector2 worldOffset,
        RiverSettings settings)
    {
        float[,] riverDepth = new float[width, height];

        // 1) Find source points (mountain peaks) using noise
        System.Random rng = new System.Random(settings.seed);
        float noiseOffsetX = (float)(rng.NextDouble() * 1000);
        float noiseOffsetZ = (float)(rng.NextDouble() * 1000);

        // 2) Generate river source points
        int sourceCount = Mathf.Max(2, (int)(width * height * settings.frequency * 0.00001f));
        for (int i = 0; i < sourceCount; i++)
        {
            int sx = rng.Next(10, width - 10);
            int sz = rng.Next(10, height - 10);

            // Only spawn rivers at high elevation
            if (heightMap[sx, sz] < 30f) continue;

            // Trace the river downhill
            TraceRiver(heightMap, riverDepth, width, height, sx, sz, settings);
        }

        return riverDepth;
    }

    // ----------------------------------------------------------
    //  Trace a single river from (startX, startZ) flowing downhill
    //  Uses greedy descent: always move to the lowest neighbor
    // ----------------------------------------------------------
    static void TraceRiver(
        float[,] heightMap,
        float[,] riverDepth,
        int width, int height,
        int startX, int startZ,
        RiverSettings settings)
    {
        int x = startX;
        int z = startZ;
        int maxSteps = width * height; // safety limit
        int step = 0;

        // Cache the highest point of this river for flow rate
        float sourceHeight = heightMap[x, z];

        while (step < maxSteps)
        {
            step++;

            // Mark this cell as a river
            float currentDepth = Mathf.Min(settings.depth, sourceHeight * 0.3f);
            riverDepth[x, z] = Mathf.Max(riverDepth[x, z], currentDepth);

            // Carve a width around the center point
            int w = Mathf.Max(1, (int)(settings.width * 0.5f));
            for (int dz = -w; dz <= w; dz++)
            {
                for (int dx = -w; dx <= w; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue;

                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > w) continue;

                    // Falloff from center
                    float falloff = 1f - (dist / w);
                    float carve = currentDepth * falloff;
                    riverDepth[nx, nz] = Mathf.Max(riverDepth[nx, nz], carve);
                }
            }

            // Find the lowest neighbor (8-directional)
            int lowestX = x;
            int lowestZ = z;
            float lowestH = heightMap[x, z];

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue;

                    // Slight randomness to make rivers curve naturally
                    float noiseInfluence = (Mathf.PerlinNoise(
                        nx * 0.1f + settings.seed * 0.01f,
                        nz * 0.1f + settings.seed * 0.01f) - 0.5f) * 2f;

                    float effectiveHeight = heightMap[nx, nz] + noiseInfluence;
                    if (effectiveHeight < lowestH)
                    {
                        lowestH = effectiveHeight;
                        lowestX = nx;
                        lowestZ = nz;
                    }
                }
            }

            // No lower neighbor found — river ends in a lake or ocean
            if (lowestX == x && lowestZ == z)
            {
                // Reached a local minimum — create a small lake
                CreateLake(heightMap, riverDepth, width, height, x, z, settings);
                break;
            }

            // Reached ground level — river empties out
            if (heightMap[lowestX, lowestZ] <= 1f) break;

            // Continue down
            x = lowestX;
            z = lowestZ;

            // Safety: don't loop forever
            if (step > maxSteps * 0.9f) break;
        }
    }

    // ----------------------------------------------------------
    //  Create a small lake at a local minimum
    // ----------------------------------------------------------
    static void CreateLake(
        float[,] heightMap,
        float[,] riverDepth,
        int width, int height,
        int cx, int cz,
        RiverSettings settings)
    {
        int lakeSize = 3;
        for (int dz = -lakeSize; dz <= lakeSize; dz++)
        {
            for (int dx = -lakeSize; dx <= lakeSize; dx++)
            {
                int nx = cx + dx;
                int nz = cz + dz;
                if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue;

                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > lakeSize) continue;

                float falloff = 1f - (dist / lakeSize);
                float depth = settings.depth * 0.5f * falloff;
                riverDepth[nx, nz] = Mathf.Max(riverDepth[nx, nz], depth);
            }
        }
    }

    // ----------------------------------------------------------
    //  Apply river carving to a height map (subtracts from heights)
    // ----------------------------------------------------------
    public static void ApplyRiverCarving(float[,] heightMap, float[,] riverDepth, int width, int height)
    {
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                heightMap[x, z] = Mathf.Max(0f, heightMap[x, z] - riverDepth[x, z]);
            }
        }
    }

    // ----------------------------------------------------------
    //  Get a smooth bank mask: 0 = deep river, 1 = dry land
    //  Used for texture blending
    // ----------------------------------------------------------
    public static float GetBankMask(float riverDepth, float bankWidth)
    {
        if (riverDepth <= 0f) return 1f;
        if (riverDepth >= bankWidth) return 0f;
        return 1f - (riverDepth / bankWidth);
    }
}
