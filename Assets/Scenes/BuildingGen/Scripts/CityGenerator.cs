using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural city generator using quad-based buildings.
/// Each building is 4 wall quads + 1 roof quad + 1 floor quad.
/// Building fronts always face the nearest road.
/// Roads are generated as textured planes between building plots.
/// </summary>
public class CityGenerator : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector – Grid
    // ─────────────────────────────────────────────
    [Header("Grid Settings")]
    [Tooltip("Number of city blocks along X axis")]
    public int gridSizeX = 4;
    [Tooltip("Number of city blocks along Z axis")]
    public int gridSizeZ = 4;
    [Tooltip("Width of roads between blocks (world units)")]
    public float roadWidth = 4f;

    // ─────────────────────────────────────────────
    //  Inspector – Building Dimensions
    // ─────────────────────────────────────────────
    [Header("Building Dimensions")]
    public float minWidth  = 4f;
    public float maxWidth  = 10f;
    public float minDepth  = 4f;
    public float maxDepth  = 10f;
    public float minHeight = 4f;
    public float maxHeight = 16f;
    [Tooltip("Padding inside each plot that the building doesn't fill (sidewalk)")]
    public float plotPadding = 0.5f;

    // ─────────────────────────────────────────────
    //  Inspector – Building Textures
    // ─────────────────────────────────────────────
    [Header("Building Textures")]
    [Tooltip("Each element is a full material set for one building type")]
    public BuildingMaterialSet[] buildingMaterialSets;

    // ─────────────────────────────────────────────
    //  Inspector – Road & Ground
    // ─────────────────────────────────────────────
    [Header("Road & Ground")]
    public Material roadMaterial;
    public Material sidewalkMaterial;
    public Material groundMaterial;

    // ─────────────────────────────────────────────
    //  Inspector – Props
    // ─────────────────────────────────────────────
    [Header("Roadside Props")]
    public PropEntry[] roadsideProps;
    [Tooltip("How many props to attempt per road segment")]
    public int propsPerRoadSegment = 3;
    [Tooltip("Minimum distance between props")]
    public float minPropSpacing = 1.5f;

    // ─────────────────────────────────────────────
    //  Inspector – Roof Props
    // ─────────────────────────────────────────────
    [Header("Roof Props")]
    public PropEntry[] roofProps;
    [Tooltip("Max roof props per building")]
    public int maxRoofProps = 3;

    // ─────────────────────────────────────────────
    //  Inspector – Streaming
    // ─────────────────────────────────────────────
    [Header("Build Streaming")]
    [Tooltip("Seconds between spawning each building. 0 = instant.")]
    public float buildDelay = 0.03f;

    // ─────────────────────────────────────────────
    //  Inspector – Container
    // ─────────────────────────────────────────────
    [Header("Hierarchy")]
    public Transform cityContainer;

    // ─────────────────────────────────────────────
    //  Private State
    // ─────────────────────────────────────────────
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private Coroutine buildRoutine;

    // Computed per-generation
    private float cellW;   // plot width  = maxWidth  + roadWidth
    private float cellD;   // plot depth  = maxDepth  + roadWidth

    // ─────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────
    public void GenerateCity(int seed)
    {
        if (buildRoutine != null) StopCoroutine(buildRoutine);
        buildRoutine = StartCoroutine(BuildCity(seed));
    }

    // ─────────────────────────────────────────────
    //  Core Coroutine
    // ─────────────────────────────────────────────
    private IEnumerator BuildCity(int seed)
    {
        Random.InitState(seed);
        ClearCity();

        cellW = maxWidth  + roadWidth;
        cellD = maxDepth  + roadWidth;

        // 1) Ground plane
        SpawnGround();

        // 2) Road network
        SpawnRoadNetwork();

        // 3) Buildings + props
        for (int px = 0; px < gridSizeX; px++)
        {
            for (int pz = 0; pz < gridSizeZ; pz++)
            {
                Vector3 plotOrigin = new Vector3(px * cellW, 0f, pz * cellD);
                SpawnBuilding(plotOrigin, px, pz);
                SpawnRoadsidePropsForPlot(plotOrigin, px, pz);

                if (buildDelay > 0f) yield return new WaitForSeconds(buildDelay);
            }
        }

        Debug.Log($"[CityGenerator] City built with seed {seed}.");
    }

    // ─────────────────────────────────────────────
    //  Ground
    // ─────────────────────────────────────────────
    private void SpawnGround()
    {
        float totalW = gridSizeX * cellW;
        float totalD = gridSizeZ * cellD;

        GameObject g = CreateQuad("Ground", new Vector3(totalW * 0.5f, -0.01f, totalD * 0.5f),
                                  Quaternion.Euler(90f, 0f, 0f),
                                  new Vector2(totalW, totalD),
                                  groundMaterial);
        Register(g);
    }

    // ─────────────────────────────────────────────
    //  Road Network
    // ─────────────────────────────────────────────
    private void SpawnRoadNetwork()
    {
        // Horizontal roads (along X) between plot rows
        for (int pz = 0; pz <= gridSizeZ; pz++)
        {
            float zCenter = pz * cellD - roadWidth * 0.5f;
            if (pz == 0) zCenter = -roadWidth * 0.5f;

            float roadLen = gridSizeX * cellW;
            GameObject road = CreateQuad(
                $"Road_H_{pz}",
                new Vector3(roadLen * 0.5f, 0f, zCenter),
                Quaternion.Euler(90f, 0f, 0f),
                new Vector2(roadLen, roadWidth),
                roadMaterial);
            Register(road);
        }

        // Vertical roads (along Z) between plot columns
        for (int px = 0; px <= gridSizeX; px++)
        {
            float xCenter = px * cellW - roadWidth * 0.5f;
            if (px == 0) xCenter = -roadWidth * 0.5f;

            float roadLen = gridSizeZ * cellD;
            GameObject road = CreateQuad(
                $"Road_V_{px}",
                new Vector3(xCenter, 0f, roadLen * 0.5f),
                Quaternion.Euler(90f, 90f, 0f),
                new Vector2(roadLen, roadWidth),
                roadMaterial);
            Register(road);
        }
    }

    // ─────────────────────────────────────────────
    //  Building
    // ─────────────────────────────────────────────
    private void SpawnBuilding(Vector3 plotOrigin, int px, int pz)
    {
        if (buildingMaterialSets == null || buildingMaterialSets.Length == 0) return;

        // Pick a material set for this building
        BuildingMaterialSet matSet;
        BuildingTypeRules rules = GetComponent<BuildingTypeRules>();
        
        if (rules != null)
        {
            matSet = buildingMaterialSets[rules.PickMaterialSetIndex(this)];
        }
        else
        {
            matSet = buildingMaterialSets[Random.Range(0, buildingMaterialSets.Length)];
        }

        // Randomise dimensions within the plot
        float bw = Random.Range(minWidth,  Mathf.Min(maxWidth,  cellW - plotPadding * 2f));
        float bd = Random.Range(minDepth,  Mathf.Min(maxDepth,  cellD - plotPadding * 2f));
        float bh = Random.Range(minHeight, maxHeight);

        // Centre the building inside the plot
        float ox = plotOrigin.x + (cellW - roadWidth) * 0.5f - bw * 0.5f + plotPadding;
        float oz = plotOrigin.z + (cellD - roadWidth) * 0.5f - bd * 0.5f + plotPadding;
        Vector3 buildingBase = new Vector3(ox, 0f, oz);

        // ── Determine which road this building's front faces ──
        // Buildings on the Z=0 edge face -Z road (south), others face north road.
        // Buildings on the X=0 edge face -X road (west), others face east road.
        // We pick front as the Z- face (south) for all, then rotate per column.
        // Simple rule: front always faces the road to the -Z side of the plot.
        // Rotation: if plot is on the south edge the front is already correct.
        // For inner plots the logic is the same since roads run on both sides.

        // Parent GO for this building
        GameObject buildingGO = new GameObject($"Building_{px}_{pz}");
        buildingGO.transform.SetParent(cityContainer, false);
        Register(buildingGO);

        Vector3 c = buildingBase + new Vector3(bw * 0.5f, bh * 0.5f, bd * 0.5f);

        // ── Four Walls ──
        // Front  (Z-)  faces road → always front material
        SpawnWallQuad(buildingGO.transform, "Wall_Front",
            new Vector3(c.x, c.y, buildingBase.z),
            Quaternion.Euler(0f, 180f, 0f),
            new Vector2(bw, bh),
            matSet.frontMaterial);

        // Back   (Z+)
        SpawnWallQuad(buildingGO.transform, "Wall_Back",
            new Vector3(c.x, c.y, buildingBase.z + bd),
            Quaternion.identity,
            new Vector2(bw, bh),
            matSet.sideMaterial);

        // Left   (X-)
        SpawnWallQuad(buildingGO.transform, "Wall_Left",
            new Vector3(buildingBase.x, c.y, c.z),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector2(bd, bh),
            matSet.sideMaterial);

        // Right  (X+)
        SpawnWallQuad(buildingGO.transform, "Wall_Right",
            new Vector3(buildingBase.x + bw, c.y, c.z),
            Quaternion.Euler(0f, -90f, 0f),
            new Vector2(bd, bh),
            matSet.sideMaterial);

        // ── Roof ──
        SpawnWallQuad(buildingGO.transform, "Roof",
            new Vector3(c.x, bh, c.z),
            Quaternion.Euler(90f, 0f, 0f),
            new Vector2(bw, bd),
            matSet.roofMaterial);

        // ── Roof Props ──
        SpawnRoofProps(buildingGO.transform, buildingBase, bw, bd, bh);
    }

    private void SpawnWallQuad(Transform parent, string name, Vector3 position,
                               Quaternion rotation, Vector2 size, Material mat)
    {
        GameObject q = CreateQuad(name, position, rotation, size, mat);
        q.transform.SetParent(parent, true);
    }

    // ─────────────────────────────────────────────
    //  Roof Props
    // ─────────────────────────────────────────────
    private void SpawnRoofProps(Transform parent, Vector3 buildingBase, float bw, float bd, float bh)
    {
        if (roofProps == null || roofProps.Length == 0) return;

        int count = Random.Range(0, maxRoofProps + 1);
        for (int i = 0; i < count; i++)
        {
            PropEntry entry = WeightedProp(roofProps);
            if (entry.prefab == null) continue;

            float rx = buildingBase.x + Random.Range(0.5f, bw - 0.5f);
            float rz = buildingBase.z + Random.Range(0.5f, bd - 0.5f);
            Vector3 pos = new Vector3(rx, bh, rz);
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject prop = Instantiate(entry.prefab, pos, rot, parent);
            // Props are children of building GO, already registered via parent
        }
    }

    // ─────────────────────────────────────────────
    //  Roadside Props
    // ─────────────────────────────────────────────
    private void SpawnRoadsidePropsForPlot(Vector3 plotOrigin, int px, int pz)
    {
        if (roadsideProps == null || roadsideProps.Length == 0) return;

        // South sidewalk edge (Z- side of plot)
        float zEdge = plotOrigin.z + plotPadding * 0.5f;
        PlacePropsAlongLine(
            new Vector3(plotOrigin.x, 0f, zEdge),
            new Vector3(plotOrigin.x + cellW - roadWidth, 0f, zEdge),
            propsPerRoadSegment, 0f);

        // West sidewalk edge (X- side of plot)
        float xEdge = plotOrigin.x + plotPadding * 0.5f;
        PlacePropsAlongLine(
            new Vector3(xEdge, 0f, plotOrigin.z),
            new Vector3(xEdge, 0f, plotOrigin.z + cellD - roadWidth),
            propsPerRoadSegment, 90f);
    }

    private void PlacePropsAlongLine(Vector3 start, Vector3 end, int attempts, float baseYaw)
    {
        float segLen = Vector3.Distance(start, end);
        List<float> placed = new List<float>();

        for (int i = 0; i < attempts; i++)
        {
            PropEntry entry = WeightedProp(roadsideProps);
            if (entry.prefab == null) continue;

            float t = Random.Range(0f, 1f);
            float dist = t * segLen;

            // Check spacing
            bool tooClose = false;
            foreach (float p in placed)
            {
                if (Mathf.Abs(p - dist) < minPropSpacing) { tooClose = true; break; }
            }
            if (tooClose) continue;

            placed.Add(dist);
            Vector3 pos = Vector3.Lerp(start, end, t);
            Quaternion rot = Quaternion.Euler(0f, baseYaw + Random.Range(-15f, 15f), 0f);
            GameObject prop = Instantiate(entry.prefab, pos, rot, cityContainer);
            Register(prop);
        }
    }

    // ─────────────────────────────────────────────
    //  Quad Mesh Helper
    // ─────────────────────────────────────────────

    /// <summary>
    /// Creates a single-sided quad mesh of the given world size,
    /// centred at <paramref name="position"/>, facing +Z in local space before rotation.
    /// </summary>
    private GameObject CreateQuad(string name, Vector3 position, Quaternion rotation,
                                  Vector2 size, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(cityContainer, false);
        go.transform.SetPositionAndRotation(position, rotation);

        MeshFilter   mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        mf.sharedMesh = BuildQuadMesh(size);
        mr.sharedMaterial = mat;

        return go;
    }

    /// <summary>
    /// Builds a quad mesh centred at origin, facing +Z (local).
    /// UVs tile based on world size so textures aren't stretched.
    /// </summary>
    private static Mesh BuildQuadMesh(Vector2 size)
    {
        float hw = size.x * 0.5f;
        float hh = size.y * 0.5f;

        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-hw, -hh, 0f),
            new Vector3( hw, -hh, 0f),
            new Vector3(-hw,  hh, 0f),
            new Vector3( hw,  hh, 0f),
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f,    0f),
            new Vector2(size.x, 0f),
            new Vector2(0f,    size.y),
            new Vector2(size.x, size.y),
        };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ─────────────────────────────────────────────
    //  Weighted Prop Picker
    // ─────────────────────────────────────────────
    private PropEntry WeightedProp(PropEntry[] props)
    {
        float total = 0f;
        foreach (var p in props) total += Mathf.Max(p.weight, 0.001f);

        float r = Random.Range(0f, total);
        float acc = 0f;
        foreach (var p in props)
        {
            acc += Mathf.Max(p.weight, 0.001f);
            if (r <= acc) return p;
        }
        return props[props.Length - 1];
    }

    // ─────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────
    private void Register(GameObject go) => spawnedObjects.Add(go);

    private void ClearCity()
    {
        foreach (var go in spawnedObjects)
            if (go != null) Destroy(go);
        spawnedObjects.Clear();
    }
}

// ─────────────────────────────────────────────────────
//  Data Structures (put in separate files if preferred)
// ─────────────────────────────────────────────────────

[System.Serializable]
public class BuildingMaterialSet
{
    [Tooltip("Display name for the Inspector")]
    public string label = "Building Type";

    [Tooltip("Front face material (faces the road) – use window/door texture")]
    public Material frontMaterial;

    [Tooltip("Side & back face material")]
    public Material sideMaterial;

    [Tooltip("Roof top material")]
    public Material roofMaterial;
}

[System.Serializable]
public class PropEntry
{
    public GameObject prefab;
    [Tooltip("Relative spawn probability. Higher = more frequent.")]
    [Range(0f, 10f)]
    public float weight = 1f;
}
