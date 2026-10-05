using UnityEngine;

/// <summary>
/// Optional component: attach alongside CityGenerator to bias which
/// BuildingMaterialSet is chosen per plot based on building type rules.
///
/// If you don't need type biasing, the CityGenerator already picks
/// material sets randomly – this component is purely additive.
///
/// Usage: place on the same GameObject as CityGenerator and it will
/// hook into generation automatically via [RequireComponent].
/// </summary>
public class BuildingTypeRules : MonoBehaviour
{
    [System.Serializable]
    public class TypeRule
    {
        public string typeName = "Office";
        [Tooltip("Indices into CityGenerator.buildingMaterialSets that belong to this type")]
        public int[] materialSetIndices = new int[] { 0 };
        [Range(0f, 10f)]
        public float spawnWeight = 1f;
    }

    [Header("Type Rules")]
    [Tooltip("Define named building types and which material sets belong to each.")]
    public TypeRule[] typeRules;

    /// <summary>
    /// Given a random value [0,1) sampled outside, return a material-set index.
    /// CityGenerator can call this instead of its own random pick.
    /// </summary>
    public int PickMaterialSetIndex(CityGenerator gen)
    {
        if (typeRules == null || typeRules.Length == 0 ||
            gen.buildingMaterialSets == null || gen.buildingMaterialSets.Length == 0)
            return Random.Range(0, gen.buildingMaterialSets.Length);

        // Weighted pick of a TypeRule
        float total = 0f;
        foreach (var r in typeRules) total += Mathf.Max(r.spawnWeight, 0.001f);

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        TypeRule chosen = typeRules[typeRules.Length - 1];

        foreach (var r in typeRules)
        {
            acc += Mathf.Max(r.spawnWeight, 0.001f);
            if (roll <= acc) { chosen = r; break; }
        }

        // Pick a random material set index from within that type
        if (chosen.materialSetIndices.Length == 0) return 0;
        return chosen.materialSetIndices[Random.Range(0, chosen.materialSetIndices.Length)];
    }
}
