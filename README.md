# Bezi Tool Dump

A collection of Unity Editor tools and utilities packaged as `.unitypackage` files.

## Packages

### Asset Browser (`Asset Browser.unitypackage`)

Generates interactive HTML previews for project assets. Supports images, videos, FBX models, and prefabs. 3D models are rendered using three.js with HDRI lighting, embedded textures as base64 data URIs, and OrbitControls for in-browser inspection — no Unity required to view them.

**Key script:** `Assets/Scripts/Editor/HTML Asset Viewer/AssetBrowserPreview.cs`

---

### Capture Tools (`Capture_Tools_Images.unitypackage`)

Three editor utilities for capturing and optimizing assets:

- **SceneCaptureWindow** — Captures the current SceneView camera angle as a PNG at configurable resolution with a custom filename prefix and output directory.
- **PrefabTurnaroundCapture** — Right-click a prefab to auto-capture four 90° turnaround shots (Front, Right, Back, Left) with transparent backgrounds, saved as PNGs.
- **MeshCombinerWindow** — Analyzes a GameObject hierarchy, merges all child meshes into a single optimized mesh organized by sub-mesh materials, and saves the result as a `.asset` file.

**Key scripts:** `Assets/Content/Tools/Editor/`

---

### Model Recorder (`Model_Recorder.unitypackage`)

Records MP4 videos of models directly from the Unity Editor. Supports two recording modes:

- **OrbitPan360** — Smoothly orbits the camera 360° around a target object.
- **WaypointPath** — Follows a user-defined spline path through a series of `CameraWaypoint` components with configurable field-of-view and dwell time per waypoint. Paths use Catmull-Rom spline interpolation and support closed-loop playback.

Output resolution, frame rate, duration, and background color are all configurable. Also includes `PrefabToFbxExporter` for exporting selected prefabs to FBX via Unity's FBX Exporter package.

**Key scripts:** `Assets/Scripts/ModelPanRecorder/`

---

### Pixel Art Tool (`PixelArtTool.unitypackage`)

A self-contained browser-based pixel art editor (`pixel-art-editor.html`). Features include a canvas drawing interface, color palette management, brush tools, undo/redo, and PNG export. No server required — open the HTML file directly.

**Key file:** `Assets/PixelArtTool/pixel-art-editor.html`

---

### FBX Import Pipeline (`FBX_Importer_v1/fbx_Importer_v1.unitypackage`)

Automates importing FBX files from an external folder into Unity, with an inspector-based UI for splitting each FBX into individual prefabs and extracting materials and textures into organized subfolders.

- **FBXImportWindow** — Editor window (`Tools > FBX Import Pipeline`) for selecting source and destination folders and triggering imports.
- **FBXImportPipeline** — Static utility handling file copying, texture/material extraction, duplicate detection, pivot adjustment, and prefab generation.
- **FBXImportTask** — ScriptableObject storing per-FBX config: root object decisions, duplicate groups, and generated prefab paths.
- **FBXImportTaskEditor** — Custom Inspector with sort modes (Hierarchy, By Appearance, By Name), duplicate highlighting, and Process/Reset buttons.

Duplicate detection compares mesh geometry (vertex count, triangle counts, bounds, materials) rather than asset names. All generated prefabs have their pivot anchored to the bottom-center of their renderer bounds.

**Key scripts:** `FBX_Importer_v1/FBXImporter/Editor/`

---

### Zoo Generator (`ZooGenerator.unitypackage`)

Procedurally populates a Unity scene with prefabs organized into categories, useful for quickly building asset showcase scenes. Configuration is driven by a `ZooSceneConfig` ScriptableObject that defines category names, prefab folder paths, layout mode (grid or auto-fit), and spacing. The layout engine uses bounds-aware packing to prevent overlaps.

**Key scripts:** `Assets/Scripts/Editor/`

## Installation

1. Open your Unity project.
2. Drag the desired `.unitypackage` file into the Project window, or go to **Assets → Import Package → Custom Package**.
3. Select the assets to import and click **Import**.
