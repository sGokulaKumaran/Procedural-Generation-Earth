using System.Collections;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class WorldGenerator : MonoBehaviour
{
    [Header("World")]
    public int worldSize = 2048;
    public int verticesPerSide = 1025;

    [Header("Generation")]
    public bool generateOnStart = true;
    public bool centerOnPlayer = true;
    public Transform player;
    public Material terrainMaterial;
    public TextureManager textureManager;

    [Header("Seed")]
    public int seed = 42;

    [Header("Terrain Shape")]
    public float baseFrequency = 0.0018f;
    [Range(1, 10)] public int octaves = 4;
    [Range(0.1f, 0.9f)] public float persistence = 0.48f;
    [Range(1.2f, 3.5f)] public float lacunarity = 2.1f;
    public float heightMult = 120f;
    [Range(0.5f, 4f)] public float exponent = 1.6f;
    public float mountainBlend = 0.55f;
    public float mountainFreq = 0.0022f;

    private MeshFilter mf;
    private MeshRenderer mr;
    private MeshCollider mc;

    private float offsetX, offsetZ;
    private bool isGenerating;
    private GenerationBox currentBox;

    private class GenerationBox
    {
        public MeshData data;
        public float[] heightMap;
        public float[] flatnessMap;
    }

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        mc = GetComponent<MeshCollider>();
    }

    void Start()
    {
        var rng = new System.Random(seed);
        offsetX = (float)(rng.NextDouble() * 200000.0 - 100000.0);
        offsetZ = (float)(rng.NextDouble() * 200000.0 - 100000.0);

        if (player == null && Camera.main != null)
            player = Camera.main.transform;

        if (terrainMaterial != null) mr.sharedMaterial = terrainMaterial;
        if (textureManager != null)
        {
            textureManager.terrainMaterial = terrainMaterial;
            textureManager.ForceRebindShader();
            textureManager.ConfigureMaterial();
        }

        if (generateOnStart) StartGeneration();
    }

    void Update()
    {
        if (!isGenerating || currentBox == null) return;
        if (currentBox.data == null) return;

        ApplyGeneratedWorld(currentBox);
        currentBox = null;
        isGenerating = false;
    }

    public void StartGeneration()
    {
        if (isGenerating) return;

        if (mf.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = null;
        }
        mc.sharedMesh = null;

        if (centerOnPlayer && player != null)
        {
            transform.position = new Vector3(
                player.position.x - worldSize * 0.5f, 0f,
                player.position.z - worldSize * 0.5f);
        }
        else
        {
            transform.position = Vector3.zero;
        }

        var box = new GenerationBox();
        currentBox = box;
        isGenerating = true;

        var gen = new WorldGeneratorThreaded
        {
            VerticesPerSide = verticesPerSide,
            WorldSize = worldSize,
            Octaves = octaves,
            BaseFrequency = baseFrequency,
            Persistence = persistence,
            Lacunarity = lacunarity,
            HeightMult = heightMult,
            Exponent = exponent,
            MountainBlend = mountainBlend,
            MountainFreq = mountainFreq,
            OffsetX = offsetX,
            OffsetZ = offsetZ,
        };

        System.Threading.Thread thread = new System.Threading.Thread(() =>
        {
            gen.Generate();
            box.data = gen.ResultData;
            box.heightMap = gen.HeightMap;
            box.flatnessMap = gen.FlatnessMap;
        });
        thread.IsBackground = true;
        thread.Start();
    }

    void ApplyGeneratedWorld(GenerationBox box)
    {
        MeshData meshData = box.data;

        if (textureManager != null && box.heightMap != null && box.flatnessMap != null)
        {
            textureManager.ApplyBiomeBlending(
                meshData, verticesPerSide, box.heightMap, box.flatnessMap);
        }

        Mesh mesh = meshData.CreateMesh();
        mf.mesh = mesh;
        if (terrainMaterial != null) mr.sharedMaterial = terrainMaterial;

        StartCoroutine(ApplyColliderNextFrame(mesh));
    }

    IEnumerator ApplyColliderNextFrame(Mesh mesh)
    {
        yield return new WaitForEndOfFrame();
        if (mc != null && this != null) mc.sharedMesh = mesh;
    }
}
