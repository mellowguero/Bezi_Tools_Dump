# FBX Import Pipeline Tool

## Overview

The FBX Import Pipeline is a custom Unity Editor tool that automates importing FBX files from an external folder, provides an inspector-based decision UI for splitting each FBX into individual prefabs, and extracts materials and textures into organized subfolders. It detects duplicate objects by mesh geometry and anchors every prefab's pivot at the bottom-center of its bounds.

## Scripts

All scripts live in `Editor/`:

```
FBXImportTask.cs           # ScriptableObject — per-FBX config, root object decisions, duplicate groups, generated prefab paths
FBXImportPipeline.cs       # Static utility — import, extract, duplicate detection, pivot adjustment, prefab generation
FBXImportWindow.cs         # EditorWindow — folder selection UI, import trigger (Tools > FBX Import Pipeline)
FBXImportTaskEditor.cs     # Custom Inspector — decision UI with sort modes, duplicate highlighting, process/reset buttons
```

## Workflow

### Phase 1 — Import
1. Open `Tools > FBX Import Pipeline` window.
2. Set the external folder containing `.fbx` files and the Unity destination folder.
3. Click **Import FBX Files**. The tool copies each FBX into Unity, extracts textures and materials, and creates an `FBXImportTask` ScriptableObject next to each FBX.

### Phase 2 — Decision and Processing
1. Select an `FBXImportTask` asset in the Project window.
2. In the Inspector, decide how to split the FBX:
   - **Keep as Single Prefab** — exports the entire FBX as one prefab.
   - **Split mode** — toggle individual root objects on/off. Use the **Sort** toolbar to view by Hierarchy Order, By Appearance (grouped by visual similarity), or By Name.
3. Click **Process Task** to generate prefabs. Each prefab's pivot is automatically placed at the bottom-center of its renderer bounds.
4. Use **Reset Task** to re-configure and reprocess if needed.

## Key Features

### Duplicate Detection
Identifies visually identical objects by comparing mesh geometry (vertex count, triangle counts per submesh, rounded bounds, materials) rather than mesh asset names. Objects like `Cylinder.000` through `Cylinder.009` that share the same geometry are grouped together. When processing, only one prefab is created per duplicate group.

### Sort by Appearance
In split mode, the root objects list can be sorted by visual similarity. Duplicate groups appear as collapsible sections with batch **All** / **None** toggle buttons and color-coded highlighting.

### Bottom Pivot Anchoring
Every generated prefab has its transform origin repositioned to the bottom-center of its combined renderer bounds, making placement on surfaces straightforward.

## Data Model

`FBXImportTask` (ScriptableObject) stores:
- `SourceFBX` — reference to the imported FBX asset
- `PrefabOutputFolder`, `MaterialsFolder`, `TexturesFolder` — output paths
- `RootObjects` — list of `RootObjectEntry` (name, split toggle, duplicate group ID)
- `DuplicateGroups` — list of `DuplicateGroup` (group ID, member names, vertex count, shared mesh name)
- `KeepAsSinglePrefab` — single vs split mode toggle
- `IsProcessed` — processing state
- `GeneratedPrefabPaths` — paths to all prefabs created during processing

## Exporting as .unity

To export the processed prefabs into a scene file:

1. Create a new scene via `File > New Scene`.
2. Drag all generated prefabs from the output folder into the scene hierarchy. Position them as needed.
3. Save the scene via `File > Save As` to your desired location (e.g., `Assets/Scenes/ExportedAssets.unity`).
4. The `.unity` file contains all placed instances and their transforms. Materials, textures, and prefabs remain as asset references within the project.

To share the full package, use `Assets > Export Package` and include the scene, prefabs, materials, and textures folders.

## Dependencies

- Unity 6 (6000.3)
- Universal Render Pipeline (URP 17.3.0)
- No external packages required
