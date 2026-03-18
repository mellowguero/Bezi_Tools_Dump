# Capture Tools

Two editor utilities for grabbing reference images from Unity — scenes and prefabs — for use in UI mockups and design work.

---

## Scene Capture Window

**Location:** `Tools > Capture > Scene Capture Window`

Snapshots the current SceneView camera angle and saves it as a PNG.

### How to Use

1. Open via **Tools > Capture > Scene Capture Window**.
2. Position the SceneView camera to the exact angle you want to capture.
3. Set your desired **Width** and **Height** (default 1920 × 1080).
4. Set a **Filename Prefix** (default `SceneCapture`).
5. Set the **Output Directory** or use the folder picker button (default `Assets/Captures/Scene`).
6. Click **Capture**.

The file is saved as `{Prefix}_{YYYYMMDD_HHMMSS}.png` and pinged in the Project window automatically.

### Notes

- The SceneView must be open and active when you click Capture.
- Gizmos and overlays are **not** included — only the rendered scene geometry.
- Resolution is clamped between 64 and 8192 px on each axis.
- Output directory is created automatically if it does not exist.

---

## Prefab Turnaround Capture

**Location:** Right-click any prefab in the Project window → **Capture Prefab Turnaround**

Renders 4 directional views of a prefab (Front, Right, Back, Left) with a **transparent background** and saves them as PNGs.

### How to Use

1. In the **Project window**, right-click a prefab asset.
2. Select **Capture Prefab Turnaround**.
3. Four PNGs are saved automatically to `Assets/Captures/Prefabs/`.

Output filenames follow the pattern: `{PrefabName}_Front.png`, `{PrefabName}_Right.png`, `{PrefabName}_Back.png`, `{PrefabName}_Left.png`.

### Notes

- The prefab must have at least one `Renderer` component — otherwise a warning dialog appears and nothing is saved.
- Camera is auto-framed based on the prefab's combined renderer bounds, so any size prefab works.
- Background is fully transparent (alpha 0) — PNGs are ready to drop directly into Figma or any design tool.
- A temporary additive scene is created and destroyed automatically — your open scenes are unaffected.
- Captured file paths are logged to the Console on success.

---

## Output Folder Structure

```
Assets/
  Captures/
    Scene/        ← Scene Capture output
    Prefabs/      ← Prefab Turnaround output
```
