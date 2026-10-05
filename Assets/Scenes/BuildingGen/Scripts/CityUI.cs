using UnityEngine;
using UnityEngine.UI;
using TMPro;   // Remove this line if you're not using TextMeshPro; use UnityEngine.UI.Text instead.

/// <summary>
/// Simple runtime UI that lets players cycle through city seeds.
/// 
/// Setup:
///  1. Create a Canvas in the scene.
///  2. Add a CityUI component to the Canvas root.
///  3. Wire up the TMP_Text / Button references in the Inspector.
///  4. Assign the SeedManager reference.
///
/// If you prefer Unity's built-in Text (non-TMP), swap TMP_Text → Text
/// and seedLabel.text → seedLabel.text  (same field name, just different type).
/// </summary>
public class CityUI : MonoBehaviour
{
    [Header("References")]
    public SeedManager seedManager;

    [Header("UI Elements")]
    public TMP_Text  seedLabel;   // Shows current seed number
    public Button    nextButton;
    public Button    prevButton;
    public TMP_InputField customSeedInput; // Optional: type a specific seed

    // ─────────────────────────────────────────────
    private void Awake()
    {
        if (nextButton != null) nextButton.onClick.AddListener(OnNext);
        if (prevButton != null) prevButton.onClick.AddListener(OnPrev);
    }

    private void Update()
    {
        // Keyboard shortcuts
        if (Input.GetKeyDown(KeyCode.RightArrow)) OnNext();
        if (Input.GetKeyDown(KeyCode.LeftArrow))  OnPrev();
        if (Input.GetKeyDown(KeyCode.Return) && customSeedInput != null) OnCustomSeed();

        // Keep label current
        UpdateLabel();
    }

    // ─────────────────────────────────────────────
    private void OnNext()
    {
        if (seedManager != null) seedManager.NextSeed();
    }

    private void OnPrev()
    {
        if (seedManager != null) seedManager.PreviousSeed();
    }

    public void OnCustomSeed()
    {
        if (customSeedInput == null || seedManager == null) return;
        if (int.TryParse(customSeedInput.text, out int seed))
            seedManager.SetSeed(seed);
    }

    private void UpdateLabel()
    {
        if (seedLabel != null && seedManager != null)
            seedLabel.text = $"Seed: {seedManager.CurrentSeed}";
    }
}
