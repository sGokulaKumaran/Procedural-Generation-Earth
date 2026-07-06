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
    public bool wantsCollider;

    // Bumped every time this (pooled) chunk instance is (re)assigned to a coordinate.
    // A background generation job captures the token at schedule time; if the token no
    // longer matches when the job completes, the chunk has since been reused for something
    // else and the result is discarded instead of being applied to the wrong coordinate.
    public int generationToken;

    [Header("Spawn / Despawn Feel")]
    public float fadeDuration = 0.35f;
    public float riseHeight = 15f;
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

    /// <summary>
    /// Assigns this chunk instance to a new world coordinate and bumps its generation
    /// token. Call this whenever a pooled chunk starts representing a different coordinate.
    /// </summary>
    public int BeginUse(Vector2Int newCoord, Vector3 worldPosition)
    {
        CacheComponents();
        coord = newCoord;
        restingPosition = worldPosition;
        transform.position = worldPosition;
        generationToken++;
        return generationToken;
    }

    /// <summary>
    /// Builds and assigns the visual mesh (geometry, normals, UVs, vertex colors).
    /// Collider assignment is a separate step (ApplyCollider) so it can be rate-limited
    /// independently — MeshCollider baking is one of the most expensive main-thread
    /// operations here and distant/low-detail chunks often don't need one at all.
    /// Returns the built Mesh on success (pass it to ApplyCollider if needed), or null on failure.
    /// </summary>
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
    /// Assigns (or clears) the collider mesh. Pass null to explicitly skip collision for
    /// this chunk (e.g. far/low-detail tiers that don't need physics at all).
    /// </summary>
    public void ApplyCollider(Mesh mesh)
    {
        CacheComponents();
        if (mc == null) return;
        mc.sharedMesh = mesh;
    }

    /// <summary>
    /// Plays the "rise up out of the ground" reveal so newly streamed-in terrain doesn't
    /// hard-pop into existence. A small random stagger keeps a whole ring of chunks from
    /// rising in perfect unison.
    /// </summary>
    public void PlaySpawnReveal()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(SpawnRevealRoutine());
    }

    IEnumerator SpawnRevealRoutine()
    {
        if (maxSpawnStagger > 0f)
            yield return new WaitForSeconds(Random.Range(0f, maxSpawnStagger));

        bool colliderWasEnabled = mc != null && mc.enabled;
        if (mc != null) mc.enabled = false;

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
        if (mc != null) mc.enabled = colliderWasEnabled;
        fadeRoutine = null;
    }

    /// <summary>
    /// Plays a "sink back into the ground" animation, then hands the chunk back via
    /// onComplete so the caller can deactivate/pool it. The GameObject stays active for
    /// the duration of the animation (a coroutine can't run on a disabled object) and is
    /// only actually disabled by the caller once this finishes.
    /// </summary>
    public void PlayDespawnAndPool(System.Action<Chunk> onComplete)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(DespawnRoutine(onComplete));
    }

    IEnumerator DespawnRoutine(System.Action<Chunk> onComplete)
    {
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
        generationToken++; // invalidate any in-flight async result tied to this instance

        if (mf != null && mf.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = null;
        }
        if (mc != null) mc.sharedMesh = null;
        if (mr != null) mr.enabled = false;
    }
}