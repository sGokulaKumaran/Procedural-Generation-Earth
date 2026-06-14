using System.Collections.Generic;
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TerrainChunk : MonoBehaviour
{
    public Material material;
    public Vector2 position;
    public int chunkSize = 256;
    public int chunkWorldSize = 256;

    // ── Neighbors used for seamless stitching ──
    public TerrainChunk neighborXPos;
    public TerrainChunk neighborXNeg;
    public TerrainChunk neighborZPos;
    public TerrainChunk neighborZNeg;

    public float baseFrequency = 0.0018f;
    public int octaves = 7;
    public float persistence = 0.48f;
    public float lacunarity = 2.1f;
    public float heightMult = 120f;
    public float exponent = 1.6f;
    public float mountainBlend = 0.55f;
    public float mountainFreq = 0.0022f;
    public float riverFrequency = 0.00045f;
    public float riverWidth = 0.045f;
    public float riverDepth = 12f;
    public float bankWidth = 0.09f;
    public float bankFlatness = 0.60f;
    public float valleyFrequency = 0.0008f;
    public float valleyDepth = 0.35f;

    public float offsetX = 0f;
    public float offsetZ = 0f;

    private List<TerrainGeneratorThreaded> activeThreads = new List<TerrainGeneratorThreaded>();

    // Called by TerrainManager
    public void StartThreadedGeneration()
    {
        TerrainGeneratorThreaded gen = new TerrainGeneratorThreaded();
        gen.position = position;
        gen.chunkSize = chunkSize;
        gen.chunkWorldSize = chunkWorldSize;
        gen.baseFrequency = baseFrequency;
        gen.persistence = persistence;
        gen.lacunarity = lacunarity;
        gen.heightMult = heightMult;
        gen.exponent = exponent;
        gen.mountainBlend = mountainBlend;
        gen.mountainFreq = mountainFreq;
        gen.riverFrequency = riverFrequency;
        gen.riverWidth = riverWidth;
        gen.riverDepth = riverDepth;
        gen.bankWidth = bankWidth;
        gen.bankFlatness = bankFlatness;
        gen.valleyFrequency = valleyFrequency;
        gen.valleyDepth = valleyDepth;
        gen.offsetX = offsetX;
        gen.offsetZ = offsetZ;

        // Start the math on a new background thread
        Thread thread = new Thread(gen.Generate);
        thread.Start();
        activeThreads.Add(gen);
    }

    // Runs every frame on the main thread
    void Update()
    {
        if (activeThreads.Count == 0) return;

        for (int i = activeThreads.Count - 1; i >= 0; i--)
        {
            TerrainGeneratorThreaded gen = activeThreads[i];
            if (gen.ResultData != null)
            {
                // The background thread is done! Apply the mesh.
                ApplyMeshData(gen.ResultData);
                activeThreads.RemoveAt(i);
            }
        }
    }

    public void ApplyMeshData(MeshData md)
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        MeshRenderer mr = GetComponent<MeshRenderer>();
        MeshCollider mc = GetComponent<MeshCollider>();

        Mesh mesh = md.CreateMesh();
        mf.mesh = mesh;
        if (mc != null) mc.sharedMesh = mesh;
        if (material != null) mr.sharedMaterial = material;
    }
}
