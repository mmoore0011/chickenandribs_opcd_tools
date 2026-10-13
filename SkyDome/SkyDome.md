# Sky Dome

An animated sky dome for GSPro golf courses. A large inside-out sphere mesh with a
looping rotation AnimationClip drives slow cloud movement without any runtime scripts,
so it survives the GSPro asset bundle export process exactly like the bird flock and
moving vehicle prefabs.

---

## How It Works

A standard Unity skybox is a shader applied to `RenderSettings` — you can't animate it
without a MonoBehaviour, which GSPro strips at export. Instead, a very large sphere
(default 1000 unit radius) has its normals flipped inward so the texture renders on the
inside surface. A Legacy AnimationClip rotates the sphere slowly around the Y axis,
making clouds appear to drift across the sky. GSPro renders the mesh as-is and does not
override it with its own skybox.

---

## Quick Start

### 1 — Get a sky image

Download a 4K JPG equirectangular panorama from [Poly Haven](https://polyhaven.com/hdris):

- Search for any outdoor sky — "overcast", "cloudy sky", "blue sky" etc.
- On the download page set **Format → JPG** and **Resolution → 4K**
- Save the `.jpg` file anywhere on your computer (Desktop, Downloads, etc.)

> **Why JPG?** HDR / EXR files store raw linear light values above 1.0 which display
> as pure white with an unlit shader. JPG is tone-mapped to the 0–1 display range.

### 2 — Open the tool

**Tools → chickenandribs → Sky Dome → Create Sky Dome**

### 3 — Browse for the JPG

Click **Browse for JPG...** and navigate to the file you downloaded. The filename
appears next to the button once selected.

### 4 — Adjust settings (optional)

| Setting | Default | Notes |
|---|---|---|
| Radius | 1000 | World units. 1000 covers most courses at 1 unit = 1 metre scale. |
| Slices | 64 | Longitude segments. 64 gives a smooth silhouette. |
| Stacks | 32 | Latitude rings. 32 is plenty. |
| Rotation Period | 30 min | One full 360° revolution. 30 min = subtle visible drift. |

### 5 — Click Create Sky Dome

The tool automatically:

1. Copies the JPG into `Assets/SkyDome/Textures/`
2. Applies correct import settings (Repeat wrap, Trilinear filter, Aniso 8, 4096 max size, High Quality compression)
3. Creates a material using the `Custom/SkyDome` shader with the texture assigned
4. Builds the inverted sphere mesh
5. Bakes the rotation AnimationClip
6. Saves a prefab to `Assets/SkyDome/Prefabs/SkyDome.prefab` and selects it

### 6 — Place in scene

Drag the prefab into your scene. Position it at the world origin **(0, 0, 0)**. If the
panorama's horizon or sun is facing the wrong direction, rotate the GameObject's **Y**
in the Inspector to taste.

---

## Assets created

| Path | Description |
|---|---|
| `Assets/SkyDome/Textures/<filename>.jpg` | Imported sky texture |
| `Assets/SkyDome/Materials/<filename>.mat` | Sky material (Custom/SkyDome shader) |
| `Assets/SkyDome/Meshes/SkyDome.asset` | Procedural inverted sphere mesh |
| `Assets/SkyDome/Animations/SkyDome_Rotation.anim` | Looping Y-rotation clip |
| `Assets/SkyDome/Prefabs/SkyDome.prefab` | Ready-to-use prefab |

---

## Already have a material?

Leave **Browse for JPG** empty and assign your existing material to the
**Or: Assign Existing Material** field instead. The dome will be built with that material.

---

## Troubleshooting

**Sky appears pure white**
You used an HDR or EXR file. Switch to the JPG version from Poly Haven.

**Sky is pixelated**
The texture Max Size is too low. Select the texture in the Project window, set Max Size
to 4096, and click Apply.

**Sky looks washed out / brighter than the original**
Unity's `Unlit/Texture` shader applies scene fog to the dome mesh. The tool uses the
`Custom/SkyDome` shader which disables fog. If the sky is still a bit bright, select the
material in `Assets/SkyDome/Materials/` and lower the **Exposure** slider (try 0.8–0.9).

**Sky is upside down**
Open `SkyDomeCreator.cs`, find the UV line in `BuildInvertedSphere`, and change
`1f - vt` to `vt`. Re-run the tool.

**Bottom edge of sphere is visible**
The sphere radius is too small for the camera height. Increase Radius and recreate.
1000 units covers most courses. For very large or elevated courses try 2000–3000.

**Clipping artifact (colored arc visible)**
The sphere exceeds the camera's far clip plane. Use the default 1000-unit radius.

**Clouds move too fast / too slow**
Adjust Rotation Period and recreate. 30 min = clearly visible drift; 60 min = very
subtle; 15 min = windy.

---

## Texture Quality Reference

| Max Size | GPU Memory (4K JPG) | Notes |
|---|---|---|
| 2048 | ~5 MB | Default — noticeably blurry at horizon |
| 4096 | ~16 MB | Recommended — sharp and practical |
| 8192 | ~64 MB | Full 8K source detail — high VRAM cost |
