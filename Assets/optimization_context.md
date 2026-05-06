# Unity Project Optimization Context
> Project: **Crop Shooter Environment Design — Meadows_Demo**
> Engine: Unity 2022.3.62f3 (DX11) — URP (Universal Render Pipeline)
> Session goal: Optimize FPS from 1.5 → target 60 FPS

---

## Current Status
| Metric | Start | Current | Note |
|---|---|---|---|
| FPS | 1.5 | **13–14** | With all effects, same camera angle |
| CPU | 677.5ms | ~168ms | Still CPU-bound |
| Batches | 11,417 | ~11,032 | Terrain detail is main cause |
| Tris | 8.1M | ~6.0M | |
| Shadow Casters | 694 | ~220 | |
| Draw Calls (Frame Debugger) | 290 | ~234 | |

---

## Project Info
- **Render Pipeline:** URP with custom Renderer Asset (`URP Asset`)
- **Terrain:** 1000×1000 units, single Terrain object, TerrainData duplicated to `TerrainData_Optimized`
- **Trees:** SpeedTree-based Oak prefabs (Oak1–Oak6), LOD0/1/2 per prefab
- **Grass/Detail:** 29 detail layers, all using **3D Prefab meshes** (not Texture2D), shader `BK/Grass`
- **Custom Shaders:** `BK/Grass`, `BK/VegetationLeaves`, `BK/Impostor`, `BK/Terrain` — none are SRP Batcher compatible
- **Post Processing Volume:** Global Volume with Tonemapping (ACES), Color Adjustments, Bloom, Vignette
- **Environment Script:** `BK_Environment Manager` — controls volumetric clouds, grass render distance, wind, fog

---

## Completed Optimizations

### 1. URP Asset / Shadow Settings
- `MainLightShadowmapResolution`: 4096 → **1024**
- `AdditionalLightsShadowmapResolution`: 4096 → **512**
- `ShadowCascadeCount`: 4 → **2**
- `ShadowDistance`: default → **50**
- `m_RequireDepthTexture`: kept ON (Water object needs it)
- `m_RequireOpaqueTexture`: kept ON (Water refraction — test turning off if water looks fine)

### 2. Directional Light
- `Shadow Type`: Soft Shadows → **Hard Shadows**
- Mode: still **Realtime** (Mixed mode was greyed out — fixed by enabling Baked GI first)

### 3. SSAO (Screen Space Ambient Occlusion)
| Setting | Before | After |
|---|---|---|
| Source | Depth Normals | **Depth** |
| Normal Quality | Medium | **Low** |
| Downsample | Off | **On** |
| Blur Quality | High | **Low** |
| Samples | Medium | **Low** |
| Falloff Distance | 300 | **80** |
| Direct Lighting Strength | 1 | **0** |

Result: `DepthNormalPrepass` reduced from **149 → ~15** draw calls

### 4. Terrain Settings
| Setting | Before | After |
|---|---|---|
| Detail Distance | 250 | **100** |
| Detail Density Scale | 0.75 | **0.3** |
| Detail Resolution Per Patch | 32 | **16** |
| Detail Resolution | 1024 | **512** |
| Base Map Distance | 1000 | **200** |
| Tree Distance | 5000 | **200** |
| Billboard Start | 50 | **150** |

### 5. LOD Cross Fade
- Disabled on all Oak tree prefabs (LOD Group → Fade Mode: `None`)
- Reason: was causing `Batch cause: Nodes have different LOD cross-fade mode` → 311 extra draw calls

### 6. Volumetric Clouds (BK_Environment Manager)
- `Volume Samples`: 50 → **10**
- `Volume Size`: 300 → **300** (kept)
- `Altitude`: **500** (kept — setting to 0 disables clouds completely)
- Result: FPS nearly doubled when clouds were disabled; tuning samples recovered most of the gain

### 7. Light Probe
- Added 1 global Light Probe Group covering the scene
- Result: grass/detail now receives approximate baked lighting → FPS improved to 13–14
- **TODO:** Add more probes in dense grass/under-tree areas for accurate shading

### 8. Cookie Atlas Resolution
- Reduced to **256** (no light cookies used in project)

---

## Identified Bottlenecks (Remaining)

### Primary — Terrain Detail Grass (NOT YET FIXED)
- **Detail instance density: 35,721,216** across 29 layers
- All layers use 3D Prefab meshes — cannot use Render Mode: Grass
- Custom `BK/Grass` shader not SRP Batcher compatible → every instance = separate draw call
- Disabling Draw Detail → FPS jumps to **30–40** (confirms this is the #1 bottleneck)
- **Fix plan:** Reduce from 29 → 8–10 layers, repaint with lower Detail Resolution

### Secondary — Terrain Mesh Draw Calls
- `DepthPrepass: ~105` — almost entirely `Draw Mesh (instanced) Terrain_Optimized`
- Unity Terrain system limitation — cannot reduce further without removing terrain
- Draw Instanced: ON ✅

### Minor — Bloom
- `UberPostProcess → Bloom: 16 draw calls`
- Can reduce by lowering Scatter (0.7 → 0.5) or Threshold (0.9 → 1.2)
- Low priority — only ~7% of total draw calls

---

## Frame Debugger Analysis Summary
| Pass | Draw Calls | Status |
|---|---|---|
| MainLightShadow | 29–32 | Will improve after Bake Lighting |
| DepthPrepass | 105–111 | Terrain limit — hard to reduce |
| DrawOpaqueObjects | 55–65 | Custom shaders block SRP Batcher |
| DrawTransparentObjects | 6–8 | TreeLeaves — transparent, cannot batch |
| SSAO | 2 | ✅ Optimized |
| Bloom | 16 | Minor, low priority |
| CopyDepth | 1 | Needed for Water |

### Key Batch Causes Found
- `SRP: Node is not compatible with SRP batcher` — Terrain shader (engine limitation)
- `SRP: Node use different shader keywords` — BK/VegetationLeaves
- `Nodes have different LOD cross-fade mode` — Fixed ✅
- `SRP: Node have different shaders` — BK/Impostor (minor)

---

## In Progress

### Bake Lighting (STARTED — waiting for bake to complete)
- Baked GI: **enabled**
- Lighting Mode: **Shadowmask**
- Directional Light Mode: **Mixed**
- Lightmapper: Progressive CPU
- Lightmap Resolution: **2 texels/unit**
- Max Lightmap Size: **1024**
- Environment Samples: **256** (consider reducing to 64)
- Directional Mode: **Directional** (consider switching to Non-Directional to halve lightmap memory)
- Expected result: `MainLightShadow` draw calls → near 0, Shadow Casters reduced significantly

---

## TODO / Next Steps (Priority Order)

1. **[ ] Wait for Bake Lighting to complete → compare FPS**
2. **[ ] Reduce detail layers 29 → 8–10** (biggest remaining FPS gain)
   - Identify essential layers (main grass types + 2–3 hero flowers)
   - Delete redundant/rarely-visible layers
   - Repaint on TerrainData_Optimized with Detail Resolution 512
3. **[ ] Add more Light Probes** in dense grass areas and under tree canopies
4. **[ ] Test turning off Opaque Texture** — check if Water still looks correct
5. **[ ] Consider Occlusion Culling bake** — low priority for outdoor scene, terrain not affected
6. **[ ] Reduce Bloom Scatter** 0.7 → 0.5 (minor gain)
7. **[ ] Switch Directional Mode to Non-Directional** after confirming lighting looks acceptable

---

## Scripts Written This Session

| File | Description |
|---|---|
| `FPSMovement.cs` | Rigidbody-based FPS movement (WASD + mouse look + jump). No CharacterController. Requires Rigidbody + CapsuleCollider. Camera can be independent with LateUpdate follow script. |
| `DuplicateTerrainData.cs` | Editor tool (Tools menu) to deep-copy TerrainData as a fully independent asset. Copies heightmap, splatmap, trees, and all detail layers independently. Auto-assigns to Terrain + TerrainCollider. |
| `HierarchyAnalyzer.cs` | Editor window (Tools menu) to analyze all scene GameObjects. Filter by name, static flags, layer, tag, component. Shows static flag breakdown per object and in statistics panel. |

---

## Key Technical Notes

- **CPU-bound** (CPU ~168ms vs GPU ~13ms) — reducing draw calls matters more than GPU effects
- **SRP Batcher incompatible** custom shaders (BK/*) — GPU Instancing is the only batching option
- **Terrain Detail** cannot use Static Batching or SRP Batcher — only GPU Instancing per layer
- **SpeedTree** trees ignore `Tree Distance` / `Billboard Start` — must use LOD Group on prefab
- **FOV effect on FPS:** lower FPS when looking at dense grass areas (frustum fills with grass patches)
- **Light Probes** are the only way grass/detail receives baked lighting (no lightmap support for terrain detail)
- **TerrainData duplicate** via Ctrl+D shares detail painting data — use `DuplicateTerrainData.cs` for a true independent copy
- **Soft Shadows** global setting in URP Asset should be OFF since Directional Light uses Hard Shadows
- **Grass render distance** also controlled by `BK_Environment Manager` script (separate from Terrain Detail Distance)

---

## Reference: Assets / Components Modified
- `URP Asset.asset` — shadow, MSAA, depth/opaque texture settings
- `URP Renderer Asset` — SSAO Renderer Feature settings
- `Terrain` component on scene Terrain object
- `TerrainData_Optimized.asset` — detail resolution, grass density
- `BK_Environment Manager` script component on Environment Manager GameObject
- `Global Volume` component — Post Processing profile (Meadows profile)
- Directional Light component — shadow type, light mode
- Oak1–Oak6 prefabs — LOD Group Fade Mode set to None
