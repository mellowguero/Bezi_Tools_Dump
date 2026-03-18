# Texture Atlas Tool

Two-phase Unity Editor toolset that combines multiple materials into a single texture atlas, then remaps prefab UVs to use the atlas material. Reduces draw calls by consolidating materials into one atlas texture and one shared URP/Lit material.

## Requirements

- Unity 6 (6000.3+)
- Universal Render Pipeline (URP 17.0+)

## Scripts

```
TextureAtlasTask.cs          ScriptableObject — atlas config, source materials, generated mapping
AtlasRemapTask.cs            ScriptableObject — remap config, target prefabs, mesh copy settings
TextureAtlasGenerator.cs     Static utility — atlas texture generation, grid layout, material creation
AtlasRemapGenerator.cs       Static utility — UV remapping, mesh copying, Read/Write auto-fix
TextureAtlasTaskEditor.cs    Custom Inspector for TextureAtlasTask
AtlasRemapTaskEditor.cs      Custom Inspector for AtlasRemapTask
TextureAtlasWindow.cs        EditorWindow — tabbed UI (Tools > Texture Atlas Generator)
```

## Quick Start

### Phase 1 — Generate Atlas

1. Open **Tools > Texture Atlas Generator**.
2. Select materials in the Project window, click **Add Selected Materials**.
3. Set cell size, max atlas size, padding, output folder, and prefix.
4. Click **Generate Atlas**.

Alternatively, create a `TextureAtlasTask` asset via **Create > Tools > Texture Atlas Task** and use its Inspector.

### Phase 2 — Remap Prefabs

1. Switch to the **Remap Prefabs** tab.
2. Assign the processed `TextureAtlasTask`.
3. Select prefabs in the Project window, click **Add Selected Prefabs**.
4. Enable **Create Mesh Copies** (recommended) to preserve original FBX meshes.
5. Click **Remap Prefabs**.

Alternatively, create an `AtlasRemapTask` asset via **Create > Tools > Atlas Remap Task** and use its Inspector.

## Features

- **Selective material replacement** — only atlas-mapped materials are swapped; non-atlas materials (glass, emissive, etc.) stay intact.
- **Flat-color UV generation** — meshes without UVs (e.g. Blender exports) get auto-generated UVs pointing to the correct atlas cell.
- **Shared vertex duplication** — vertices shared across submeshes are duplicated for independent UV remapping.
- **Automatic Read/Write fix** — enables `isReadable` on FBX ModelImporters before processing.
- **Multi-map atlas** — generates atlas textures for `_BaseMap`, `_BumpMap`, `_MetallicGlossMap`, `_OcclusionMap`, `_EmissionMap`.
