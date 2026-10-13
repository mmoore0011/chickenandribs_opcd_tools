# Video Animations Toolset

Converts a PNG image sequence (with alpha transparency) into a looping WebM video, then sets up a ready-to-use Unity Quad prefab to display it in a scene.

Designed for background animated elements — birds, smoke, fire, water effects — rendered from Blender or any tool that outputs RGBA PNG sequences.

---

## File Locations

| File | Location |
|---|---|
| `png2webm.py` | `Video Animations/` |
| `README.md` | `Video Animations/` |
| `MakeQuadFromVideo.cs` | `Assets/Video Animations/Editor/` |
| Output prefabs | `Assets/Video Animations/Prefabs/{video name}/` |

> The `.cs` file must live under `Assets/` so Unity compiles it. Everything else lives in `Video Animations/` alongside this README.

---

## Requirements

**Python tool (`png2webm.py`)**
- Python 3
- ffmpeg installed and available in your system PATH
  - Download: https://ffmpeg.org/download.html
  - On Windows, add the `bin` folder to Environment Variables → PATH

**Unity tool (`MakeQuadFromVideo.cs`)**
- Unity 2018.2 or later
- VideoPlayer module (included by default)
- A WebM video with alpha channel imported into your project

---

## Workflow

### Step 1 — Render your PNG sequence

Render your animation as RGBA PNG frames with a transparent background.

- In Blender: Output → PNG, Color = RGBA, Film Transparent = enabled
- Frame files can be named anything — the tool sorts them numerically
- Store all frames for one animation in a single folder (e.g. `C:/renders/my_effect/`)

### Step 2 — Convert to WebM (`png2webm.py`)

Open a terminal in the `Video Animations/` folder and run:

```
python png2webm.py
```

When prompted, paste or type the path to your PNG frames folder:

```
Path to PNG frames folder: C:/renders/my_effect
```

The script will:
- Sort all PNGs in that folder numerically
- Build a VP8 WebM video with alpha transparency
- Save it as `C:/renders/my_effect.webm` (named after the folder)
- Print the full output path when done

**Configuration** — open `png2webm.py` and adjust these two lines at the top if needed:

```python
SOURCE_FPS = 24.0   # frame rate of the source animation
FRAME_STEP = 1      # 1 = every frame rendered, 4 = every 4th frame, etc.
```

The playback speed of the video = `SOURCE_FPS / FRAME_STEP`. If you rendered every frame of a 24fps animation, both stay at their defaults (24fps output).

### Step 3 — Import into Unity

Drag the `.webm` file into your Unity project. Place it anywhere under `Assets/`.

### Step 4 — Create the Quad prefab

1. In the Unity **Project panel**, click the `.webm` file to select it
2. Go to **Tools → chickenandribs → Video Animations → Create Quad from Video**
   - The menu item is greyed out unless a VideoClip is selected
3. Unity creates a folder at `Assets/Video Animations/Prefabs/{video name}/` containing:
   - `{name}_RenderTex.renderTexture` — RenderTexture sized to match the video
   - `{name}_Mat.mat` — Unlit/Transparent material using the RenderTexture
   - `{name}_Quad.prefab` — Quad with VideoPlayer and material wired up

### Step 5 — Place in scene

- Drag `{name}_Quad.prefab` from the Project panel into the scene
- Scale it to the appropriate size for your scene
- If the video appears on the wrong side, rotate Y by 180°
- Press Play — the video loops automatically

---

## Notes

- **Alpha transparency**: the WebM uses VP8 with `yuva420p` pixel format, which Unity 2018+ supports on Windows. The `Unlit/Transparent` shader renders the alpha channel correctly.
- **No scripts required at runtime**: VideoPlayer and the Animation components used are Unity built-ins. The prefab is compatible with GSPro asset bundle export.
- **Multiple videos**: each video gets its own subfolder under `Assets/Video Animations/Prefabs/` so assets never conflict.
- **RenderTexture size**: automatically matched to the video's actual resolution.
