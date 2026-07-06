using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages seed history so the player can browse Previous / Next city layouts.
/// Wire up buttons in the Inspector or call from UI scripts.
/// </summary>
public class SeedManager : MonoBehaviour
{
    [Header("References")]
    public CityGenerator cityGenerator;

    [Header("Seed History (read-only at runtime)")]
    public List<int> seedHistory = new List<int>();
    private int currentIndex = -1;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    private void Start()
    {
        NextSeed();
    }

    // ─────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────

    /// <summary>Advance to the next seed (generates a new one if at end of history).</summary>
    public void NextSeed()
    {
        if (currentIndex < seedHistory.Count - 1)
        {
            currentIndex++;
        }
        else
        {
            int newSeed = Random.Range(int.MinValue, int.MaxValue);
            seedHistory.Add(newSeed);
            currentIndex = seedHistory.Count - 1;
        }

        Apply();
    }

    /// <summary>Go back to the previous seed in history.</summary>
    public void PreviousSeed()
    {
        if (currentIndex > 0)
        {
            currentIndex--;
            Apply();
        }
    }

    /// <summary>Generate a city from a specific seed value.</summary>
    public void SetSeed(int seed)
    {
        // Remove any "future" history beyond current position
        if (currentIndex < seedHistory.Count - 1)
            seedHistory.RemoveRange(currentIndex + 1, seedHistory.Count - currentIndex - 1);

        seedHistory.Add(seed);
        currentIndex = seedHistory.Count - 1;
        Apply();
    }

    /// <summary>Returns the seed currently displayed.</summary>
    public int CurrentSeed => (currentIndex >= 0 && currentIndex < seedHistory.Count)
                              ? seedHistory[currentIndex] : 0;

    // ─────────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────────
    private void Apply()
    {
        if (cityGenerator != null)
            cityGenerator.GenerateCity(seedHistory[currentIndex]);

        Debug.Log($"[SeedManager] Seed: {seedHistory[currentIndex]}  (History index {currentIndex}/{seedHistory.Count - 1})");
    }
}
