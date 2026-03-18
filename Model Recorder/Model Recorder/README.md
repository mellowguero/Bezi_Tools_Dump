# Model Recorder

Editor tool that records MP4 video of any scene object using either an automatic 360-degree orbit or a hand-placed waypoint path with Catmull-Rom spline interpolation. The camera always faces the target, supports per-waypoint FOV and dwell times, and outputs timestamped MP4 files via Unity's MediaEncoder API.

## Requirements

- Unity 6 (6000.3+)
- Universal Render Pipeline (URP 17.0+)
- Unity Media Encoder (`UnityEditor.Media`)

## Folder Structure

```
Model Recorder/
├── Editor/
│   ├── ModelPanRecorder.cs      EditorWindow — recording UI, orbit + waypoint modes, frame encoding
│   └── WaypointPathEditor.cs    Custom Inspector — waypoint summary, one-click waypoint creation
└── Runtime/
    ├── CameraWaypoint.cs        MonoBehaviour — per-waypoint FOV, dwell time, Scene view gizmos
    └── WaypointPath.cs          MonoBehaviour — path container, linear/spline evaluation, gizmo drawing
```

## Quick Start

### 360 Orbit Recording

1. Open **Tools > Record 360 Pan**.
2. Select a GameObject with renderers in the scene.
3. Adjust resolution, frame rate, duration, orbit distance, and camera height.
4. Click **Record 360 Pan**.

### Waypoint Path Recording

1. Create an empty GameObject, add the `WaypointPath` component.
2. Set the **Look-At Target** to the object the camera should face.
3. In the `WaypointPath` Inspector, click **Add Waypoint** — each waypoint spawns at the current Scene view camera position.
4. Adjust per-waypoint **FOV** and **Dwell Time** on each `CameraWaypoint`.
5. Open **Tools > Record 360 Pan**, switch mode to **Waypoint Path**, assign the path, and click **Record Waypoint Path**.

Output is saved to `Assets/Recordings/{Name}_{Timestamp}.mp4`.

## Features

- **Dual recording modes** — automatic 360 orbit around any renderer-bearing object, or fully custom waypoint paths.
- **Catmull-Rom spline interpolation** — smooth camera movement between waypoints with automatic tangent calculation; linear mode also available.
- **Per-waypoint FOV and dwell time** — FOV interpolates between waypoints, dwell time holds the camera at a waypoint before continuing.
- **Closed loop support** — waypoint paths can loop back to the first waypoint for seamless cycles.
- **Scene view gizmos** — waypoints render as colored spheres with FOV indicators; the path draws as a spline curve with look-at target lines.
- **Cancellable progress bar** — recording can be interrupted mid-encode without corrupting the project.
- **4x MSAA render texture** — anti-aliased output at configurable resolution (default 1920x1080, 30 FPS).

## Architecture Notes

- `ModelPanRecorder` is the core editor window. It creates a temporary hidden camera with `UniversalAdditionalCameraData`, renders frame-by-frame to a `RenderTexture`, reads pixels into a `Texture2D`, and feeds them to a `MediaEncoder`. Both recording modes share the same `RecordFrames` loop — only the per-frame positioning callback differs.
- `WaypointPath` owns the evaluation logic. `Evaluate(t, totalDuration)` maps normalized time to a position by walking through dwell periods and travel segments sequentially. Spline control points wrap correctly for both open and closed loops.
- `CameraWaypoint` is a data-only component. It stores FOV and dwell time and draws gizmos — no update logic runs at runtime.
- `WaypointPathEditor` adds an **Add Waypoint** button that creates a child `CameraWaypoint` at the current Scene view camera position, registered with Undo.
- All temporary objects (`Camera`, `RenderTexture`, `Texture2D`) use `HideAndDontSave` flags and are cleaned up in a `finally` block regardless of success or failure.
