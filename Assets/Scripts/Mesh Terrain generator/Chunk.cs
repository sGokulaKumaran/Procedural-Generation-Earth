using System.Collections;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    public Vector2Int coord;
    public int resolutionTier;
    public int verticesPerSide;
    public float worldSize;
    public bool wantsCollider;        // (kept for back-compat; not used by the new flow)

    // Bumped every time this (pooled) chunk instance is (re)assigned to a coordinate.
    // A background generation job captures the token at schedule time; if the token no
    // longer matches when the job completes, the chunk has since been reused for
    // something else and the result is discarded instead of being applied to the
    // wrong coordinate.
    public int generationToken;

    // Single source of truth: "is a MeshCollider.sharedMesh currently assigned?"
    // The new ChunkManager.SyncActiveColliders() reads this every frame to detect
    // when a chunk's desired collider state (in/out of player radius) has diverged
    // from reality, and reconciles it.
    public bool hasCollider;

    [Header("Spawn / Despawn Feel")]
    public float fadeDuration = 0.35f;
    public float riseHeight = 20f;          // bumped a bit since heightMult is higher now
    public float maxSpawnStagger = 0.12f;

    public enum State { Idle, Generating, MeshReady, Active, Unloading }
    public State state = State.Idle;

    public MeshFilter mf;
    public MeshRenderer mr;
    public MeshCollider mc;

    Vector3 restingPosition;
    Coroutine fadeRoutine;

    void Awake() => CacheComponents();

    void OnEnable()
    {
        CacheComponents();
        if (mr != null) mr.enabled = true;
    }

    void CacheComponents()
    {
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mr == null) mr = GetComponent<MeshRenderer>();
        if (mc == null) mc = GetComponent<MeshCollider>();
    }

    public int BeginUse(Vector2Int newCoord, Vector3 worldPosition)
    {
        CacheComponents();
        coord = newCoord;
        restingPosition = worldPosition;
        transform.position = worldPosition;
        generationToken++;
        return generationToken;
    }

    public Mesh ApplyVisualMesh(MeshData data)
    {
        CacheComponents();

        if (data == null || mf == null || mr == null || mc == null)
        {
            Debug.LogError($"[Chunk {coord}] ApplyVisualMesh: missing data or components.");
            return null;
        }

        if (data.vertices == null || data.vertices.Length == 0 ||
            data.triangles == null || data.triangles.Length == 0)
        {
            Debug.LogError($"[Chunk {coord}] ApplyVisualMesh: empty mesh data.");
            return null;
        }

        if (mf.sharedMesh != null)
        {
            // Visual mesh is per-chunk-instance; safe to destroy on replace.
            // The collider has its own (cached) mesh and is NOT touched here.
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = null;
        }

        Mesh mesh;
        try
        {
            mesh = data.CreateMesh();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Chunk {coord}] CreateMesh failed: {e.Message}\n{e.StackTrace}");
            return null;
        }

        if (mesh == null || mesh.triangles == null || mesh.triangles.Length == 0)
        {
            Debug.LogError($"[Chunk {coord}] Built mesh has no triangles.");
            if (mesh != null) Destroy(mesh);
            return null;
        }

        mf.sharedMesh = mesh;
        mr.enabled = true;
        state = State.Active;
        return mesh;
    }

    /// <summary>
    /// Assigns (or clears) the collider mesh. Pass null to explicitly remove
    /// the collider (e.g. far chunks outside colliderRadius). Keeps
    /// hasCollider in sync as the single source of truth.
    /// </summary>
    public void ApplyCollider(Mesh mesh)
    {
        CacheComponents();
        if (mc == null) return;
        mc.sharedMesh = mesh;
        hasCollider = (mesh != null);
    }

    public void PlaySpawnReveal()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(SpawnRevealRoutine());
    }

    IEnumerator SpawnRevealRoutine()
    {
        if (maxSpawnStagger > 0f)
            yield return new WaitForSeconds(Random.Range(0f, maxSpawnStagger));

        // IMPORTANT: the collider is NO LONGER disabled during the rise.
        // The previous version did:
        //     if (mc != null) mc.enabled = false;
        //     ... animate ...
        //     if (mc != null) mc.enabled = colliderWasEnabled;
        // which made the player fall through the chunk for the 0.35s reveal.
        // The MeshCollider and the MeshFilter share this GameObject's transform,
        // so they rise together. Leaving the collider enabled keeps the player
        // grounded on the rising terrain.

        Vector3 target = restingPosition;
        Vector3 start = target + Vector3.down * riseHeight;
        transform.position = start;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / fadeDuration);
            float eased = 1f - (1f - p) * (1f - p); // ease-out
            transform.position = Vector3.Lerp(start, target, eased);
            yield return null;
        }

        transform.position = target;
        fadeRoutine = null;
    }

    public void PlayDespawnAndPool(System.Action<Chunk> onComplete)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(DespawnRoutine(onComplete));
    }

    IEnumerator DespawnRoutine(System.Action<Chunk> onComplete)
    {
        // During the despawn sink it's fine (and cheaper) to disable the collider
        // — the chunk is leaving the world.
        if (mc != null) mc.enabled = false;

        Vector3 start = transform.position;
        Vector3 end = start + Vector3.down * riseHeight;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / fadeDuration);
            float eased = p * p; // ease-in
            transform.position = Vector3.Lerp(start, end, eased);
            yield return null;
        }

        // Restore enabled state — Hide() will null out the sharedMesh next anyway.
        if (mc != null) mc.enabled = true;

        fadeRoutine = null;
        onComplete?.Invoke(this);
    }

    public void Hide()
    {
        CacheComponents();
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (mr != null) mr.enabled = false;
        if (mc != null) mc.sharedMesh = null;
        hasCollider = false;
    }

    public void Reset()
    {
        CacheComponents();
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        coord = default;
        resolutionTier = 0;
        state = State.Idle;
        wantsCollider = false;
        generationToken++;

        if (mf != null && mf.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = null;
        }
        if (mc != null) mc.sharedMesh = null;
        if (mr != null) mr.enabled = false;
        hasCollider = false;
    }
}
