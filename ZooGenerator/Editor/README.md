# Zoo Scene Generator

Editor tool that batch-instantiates prefabs from project folders into the active scene, organized by category with automatic layout. Supports two layout modes — fixed grid and bounds-aware auto-fit packing — with a preview mode that places wireframe placeholders instead of full prefabs. Categories are arranged along the X axis with configurable spacing to prevent overlap, and all settings are stored in reusable ScriptableObject configs.

## Requirements

- Unity 6 (6000.3+)
- Universal Render Pipeline (URP 17.0+)

## Folder Structure

```
Editor/
├── ZooSceneConfig.cs              ScriptableObject — categories, layout mode, grid/auto-fit settings, preview toggle
├── ZooSceneGeneratorWindow.cs     EditorWindow — config management, category list UI, generate/clear buttons
├── ZooSceneGenerator.cs           Static utility — category iteration, prefab instantiation, world-space cursor
└── ZooPrefabLayoutEngine.cs       Static utility — grid and auto-fit position computation, prefab bounds calculation
```

## Quick Start

1. Open **Window > Zoo Scene Generator**.
2. Click **New Config** to create a `ZooSceneConfig` asset, or assign an existing one.
3. Click **+ Add Category**, set a display name and browse to a folder containing prefabs.
4. Choose **Grid** or **AutoFit** layout mode and adjust spacing/padding.
5. Click **Generate Scene** to instantiate all prefabs under a `[ZooScene]` root object.
6. Click **Clear Scene** to remove all generated content.

Alternatively, create a config asset via **Create > Zoo Scene > Config** and assign it in the window.

## Features

- **Two layout modes** — Grid uses fixed column count and spacing; AutoFit packs prefabs by their actual XZ footprint with row wrapping at a configurable max width.
- **Bounds-aware placement** — prefab bounds are computed from all child MeshFilters without instantiation, transforming mesh corners into root-local space.
- **Preview mode** — replaces prefab instances with disabled-renderer cubes scaled to prefab bounds for fast iteration on large asset libraries.
- **Category spacing** — a world-space cursor advances along X after each category's footprint, preventing overlap between groups.
- **Config persistence** — all settings live in ScriptableObject assets with New Config, Save As, and hot-swap support in the window.
- **Full undo support** — generation and clear operations are registered with Unity's Undo system as a single group.

## Architecture Notes

- `ZooSceneConfig` is the data layer. It holds a list of `CategoryDefinition` (display name + folder path), layout mode enum, per-mode settings structs (`GridSettings`, `AutoFitSettings`), category spacing, and preview flag. All fields are serializable for Inspector editing and window binding.
- `ZooSceneGenerator.Generate()` is the orchestrator. It iterates categories, queries prefab GUIDs via `AssetDatabase.FindAssets`, delegates position computation to the layout engine, then instantiates prefabs (or preview cubes) under per-category container GameObjects parented to a `[ZooScene]` root. Asset loading is batched inside `StartAssetEditing`/`StopAssetEditing` for performance.
- `ZooPrefabLayoutEngine` is stateless. `ComputeGridPositions` maps index to column/row. `ComputeAutoFitPositions` walks a cursor across X, wrapping rows when exceeding `maxRowWidth`. `ComputePrefabBounds` transforms all 8 mesh-bounds corners per MeshFilter into root space without instantiating the prefab. `ComputeCategoryFootprint` merges per-prefab bounds at their layout positions into a single enclosing bounds used for category cursor advancement.
- `ZooSceneGeneratorWindow` binds to `ZooSceneConfig` through `SerializedObject`/`SerializedProperty` for proper undo, multi-edit, and dirty-state tracking. Config creation and duplication go through `AssetDatabase.CreateAsset`.
