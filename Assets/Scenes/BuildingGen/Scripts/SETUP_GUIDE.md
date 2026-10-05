# Procedural City Generator — Setup Guide

## Scripts Overview

| File | Purpose |
|---|---|
| `CityGenerator.cs` | Core system — quads, roads, props, roof assets |
| `SeedManager.cs` | Seed history navigation (Next / Prev / Custom) |
| `BuildingTypeRules.cs` | Optional: bias shop/house/office type ratios |
| `CityUI.cs` | Runtime UI buttons + seed label |

---

## Architecture

### Why Quads, Not Cubes?

Each building is **6 quads** (4 walls + 1 roof + no floor needed):
- Zero overdraw inside the building — no hidden faces
- Each face gets its own UV space → textures scale correctly per face
- One material per face type → GPU batching works efficiently
- Changing a building's look = swap one material, not hundreds of cubes

### Road & Building Layout

```
┌──────┬─road─┬──────┬─road─┐
│ plot │      │ plot │      │
│  00  │  R   │  10  │  R   │
├──────┤      ├──────┤      │
│ road │      │ road │      │
├──────┼──────┼──────┼──────┤
│ plot │      │ plot │      │
│  01  │  R   │  11  │  R   │
└──────┴─road─┴──────┴─road─┘
```

- Every building front (`Wall_Front`) faces **-Z** (south), toward the road below it.
- Roads are flat quads with a tiling `roadMaterial`.
- Sidewalk props spawn between the road edge and the building footprint.

---

## Step-by-Step Setup in Unity

### 1. Create the Scene Hierarchy

```
Scene
 └─ CityRoot  (Empty GameObject)
     ├─ CityGenerator.cs
     └─ SeedManager.cs
```

Set `CityGenerator.cityContainer` → the **CityRoot** Transform.  
Set `SeedManager.cityGenerator` → the **CityGenerator** component.

### 2. Create Building Material Sets

For each building *type* (e.g. Office, Shop, House, Apartment) create a **BuildingMaterialSet** entry in the Inspector:

| Field | Texture to use |
|---|---|
| `frontMaterial` | Facade with windows / door — faces the road |
| `sideMaterial` | Side / back wall texture |
| `roofMaterial` | Roof / flat top texture |

Recommended minimum: **3–5 sets** for visual variety.

#### Material Setup Tips
- Set **Tiling** on the material (e.g. `X=1, Y=2`) to repeat the texture per floor.
- Use **Unlit/Texture** shader for a clean look without lighting artifacts.
- For windows: bake a pre-lit texture so windows glow at "night" without real-time lights.

### 3. Road & Ground Materials

| Inspector Field | Texture |
|---|---|
| `roadMaterial` | Road / asphalt (tiled, set tiling = road length) |
| `sidewalkMaterial` | Pavement / concrete (currently unused, available for extension) |
| `groundMaterial` | Dirt / grass for areas outside plots |

### 4. Roadside Props

Add entries to `Roadsides Props` array. Each `PropEntry` has:

| Field | Description |
|---|---|
| `prefab` | A pre-made prefab (streetlight, bench, trash can, signboard, tree…) |
| `weight` | Relative spawn chance. `2` = twice as common as `1` |

**Suggested props:**
- Streetlight pole
- Park bench
- Trash can / dumpster
- Road sign
- Small tree / shrub
- Newspaper stand
- Vending machine

### 5. Roof Props

Add entries to `Roof Props` array. Same `PropEntry` format.

**Suggested props:**
- AC ventilator unit
- Water tank
- Satellite dish
- Antenna / cell tower segment
- Rooftop signage box
- Exhaust fan duct

### 6. Grid Settings

| Field | Recommendation |
|---|---|
| `gridSizeX/Z` | 4–6 for a small city |
| `roadWidth` | 4–6 units (matches your road texture width) |
| `buildDelay` | 0 for instant, 0.03 for cinematic build-in |

### 7. Building Dimensions

Set `minWidth`, `maxWidth`, `minDepth`, `maxDepth` so buildings fit inside a cell:

```
cellWidth = maxWidth + roadWidth
```

Buildings are randomly sized between min and max **per seed**, so a 4×4 grid
with `maxWidth=10, roadWidth=4` creates 14-unit-wide cells.

### 8. UI Setup (Optional)

1. Create a **Canvas** (Screen Space – Overlay).
2. Add two **Buttons** (Next City / Prev City).
3. Add a **TextMeshPro Text** for the seed display.
4. Add `CityUI.cs` to the Canvas root.
5. Wire up all references in Inspector.
6. Keyboard shortcuts: `← →` arrows also work.

---

## Extending the System

### Add More Building Variation

Use `BuildingTypeRules.cs` to define named types:

```
Office  → materialSetIndices: [0, 1]   weight: 3
Shop    → materialSetIndices: [2, 3]   weight: 4
House   → materialSetIndices: [4]      weight: 2
```

Then call `BuildingTypeRules.PickMaterialSetIndex(cityGenerator)` from a
subclass of `CityGenerator` that overrides the material-picking logic.

### Animated Build-In Effect

Set `buildDelay = 0.05f` — buildings appear one at a time coroutine-streamed.
Combine with a "rising" animation on each building by starting Y below ground
and lerping to final height in the instantiation call.

### LOD / Culling

For larger cities, add `LODGroup` components to building prefabs or use
Unity's `Occlusion Culling` (baked) since all buildings are static once placed.

### Saving Seeds

`SeedManager.seedHistory` is a public `List<int>` — serialize it to `PlayerPrefs`
or a JSON file to let players bookmark favourite cities:

```csharp
PlayerPrefs.SetString("SeedHistory", JsonUtility.ToJson(new SeedListWrapper(seedHistory)));
```

---

## Quick Inspector Reference

```
CityGenerator
 ├── Grid Settings
 │    ├─ gridSizeX          = 4
 │    ├─ gridSizeZ          = 4
 │    └─ roadWidth          = 4
 ├── Building Dimensions
 │    ├─ minWidth/maxWidth  = 4 / 10
 │    ├─ minDepth/maxDepth  = 4 / 10
 │    └─ minHeight/maxHeight= 4 / 16
 ├── Building Textures
 │    └─ buildingMaterialSets[]
 │         ├─ [0] Office   frontMat / sideMat / roofMat
 │         ├─ [1] Shop     frontMat / sideMat / roofMat
 │         └─ [2] House    frontMat / sideMat / roofMat
 ├── Road & Ground
 │    ├─ roadMaterial
 │    └─ groundMaterial
 ├── Roadside Props
 │    ├─ [0] Streetlight  weight=2
 │    ├─ [1] Bench        weight=1
 │    └─ [2] TrashCan     weight=3
 ├── Roof Props
 │    ├─ [0] AC_Unit      weight=3
 │    ├─ [1] WaterTank    weight=1
 │    └─ [2] Antenna      weight=2
 ├── Build Streaming
 │    └─ buildDelay       = 0.03
 └── Hierarchy
      └─ cityContainer    = CityRoot Transform
```
