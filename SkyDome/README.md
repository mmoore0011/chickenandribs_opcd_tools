# Sky Dome

## Description

An animated sky dome for GSPro golf courses. A large inside-out sphere
mesh with a looping rotation AnimationClip drives slow cloud movement
without any runtime scripts, so it survives the GSPro asset bundle export
process exactly like the bird flock and moving vehicle prefabs.

**How it works:** a standard Unity skybox is a shader applied to
`RenderSettings` — you can't animate it without a MonoBehaviour, which
GSPro strips at export. Instead, a very large sphere (default 1000 unit
radius) has its normals flipped inward so the texture renders on the
inside surface. A Legacy AnimationClip rotates the sphere slowly around
the Y axis, making clouds appear to drift across the sky. GSPro renders
the mesh as-is and does not override it with its own skybox.

## Installation

**Option A — copy into your project**: copy the `SkyDome` folder into
your Unity project's `Assets` folder; Unity compiles it automatically.

**Option B — import the `.unitypackage`**: **Assets → Import Package →
Custom Package…** and select `skydome.unitypackage`.

## Step-by-Step Instructions

### 1 — Get a sky image

Download a 4K JPG equirectangular panorama from
[Poly Haven](https://polyhaven.com/hdris): search for any outdoor sky
("overcast", "cloudy sky", "blue sky", etc.), set **Format → JPG** and
**Resolution → 4K** on the download page, and save the file anywhere.

> **Why JPG, not HDR/EXR?** HDR/EXR files store raw linear light values
> above 1.0, which display as pure white with an unlit shader. JPG is
> tone-mapped to the 0–1 display range.

### 2 — Open the tool

**Tools → chickenandribs → Sky Dome → Create Sky Dome**

### 3 — Browse for the JPG

Click **Browse for JPG...** and navigate to the downloaded file.

### 4 — Adjust settings (optional)

| Setting | Default | Notes |
|---|---|---|
| Radius | 1000 | World units. Covers most courses at 1 unit = 1 metre scale. |
| Slices | 64 | Longitude segments — 64 gives a smooth silhouette. |
| Stacks | 32 | Latitude rings — 32 is plenty. |
| Rotation Period | 30 min | One full 360° revolution. 30 min = subtle visible drift. |

### 5 — Click Create Sky Dome

The tool automatically copies the JPG into `Assets/SkyDome/Textures/`,
applies correct import settings, creates a material using the
`Custom/SkyDome` shader, builds the inverted sphere mesh, bakes the
rotation AnimationClip, and saves a prefab to
`Assets/SkyDome/Prefabs/SkyDome.prefab`.

### 6 — Place in scene

Drag the prefab into your scene, positioned at the world origin
**(0, 0, 0)**. If the panorama's horizon or sun faces the wrong
direction, rotate the GameObject's **Y** in the Inspector to taste.

### Already have a material?

Leave **Browse for JPG** empty and assign your existing material to the
**Or: Assign Existing Material** field instead — the dome is built with
that material directly.

### Assets created

| Path | Description |
|---|---|
| `Assets/SkyDome/Textures/<filename>.jpg` | Imported sky texture |
| `Assets/SkyDome/Materials/<filename>.mat` | Sky material (Custom/SkyDome shader) |
| `Assets/SkyDome/Meshes/SkyDome.asset` | Procedural inverted sphere mesh |
| `Assets/SkyDome/Animations/SkyDome_Rotation.anim` | Looping Y-rotation clip |
| `Assets/SkyDome/Prefabs/SkyDome.prefab` | Ready-to-use prefab |

## Troubleshooting

- **Sky appears pure white** — you used an HDR or EXR file; switch to the
  JPG version from Poly Haven.
- **Sky is pixelated** — texture Max Size is too low. Select the texture
  in the Project window, set Max Size to 4096, click Apply.
- **Sky looks washed out / brighter than the original** — `Unlit/Texture`
  applies scene fog to the dome; this tool's `Custom/SkyDome` shader
  already disables fog, but if it's still a bit bright, lower the
  **Exposure** slider on the material (try 0.8–0.9).
- **Sky is upside down** — open `SkyDomeCreator.cs`, find the UV line in
  `BuildInvertedSphere`, change `1f - vt` to `vt`, and re-run the tool.
- **Bottom edge of sphere is visible** — the radius is too small for the
  camera height. Increase Radius and recreate (1000 covers most courses;
  try 2000–3000 for very large or elevated ones).
- **Clipping artifact (colored arc visible)** — the sphere exceeds the
  camera's far clip plane. Use the default 1000-unit radius.
- **Clouds move too fast / too slow** — adjust Rotation Period and
  recreate (30 min = clearly visible drift, 60 min = very subtle, 15 min
  = windy).

**Texture quality reference:**

| Max Size | GPU Memory (4K JPG) | Notes |
|---|---|---|
| 2048 | ~5 MB | Default — noticeably blurry at horizon |
| 4096 | ~16 MB | Recommended — sharp and practical |
| 8192 | ~64 MB | Full 8K source detail — high VRAM cost |

## Feature Requests / Bug Reports

Please open an issue in this repository.
