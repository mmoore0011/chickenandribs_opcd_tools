# OPCD MAHS Setup Tool

Automates the full OPCD texture pipeline in one click: packs a MAHS map, expands the texture arrays, and creates a ready-to-use material.

## Installation

Copy `MAHSSetupWindow.cs` into any folder named **Editor** inside your Unity project's `Assets/` folder.  
Open via **Tools > chickenandribs > MAHS Setup** in the Unity menu bar.

---

## What the tool does

**Important:** the tool does **not** pack channels itself — it expects your source folder to already
contain a pre-packed `_MAHS.png` (R = Metalness, G = AO, B = Displacement, A = Smoothness). It looks
for exactly four files by suffix: `_Albedo`, `_MAHS`, `_Normal` (all required), and `_Height` (optional,
used only to auto-assign the height map on the material). If your source doesn't already come as a
packed MAHS file — which is every workflow below except a Quixel Mixer export configured with a custom
MAHS-style pack preset — pack one first with **MAHSPackerWindow.cs** (Tools > chickenandribs > MAHS
Texture Packer), which takes your raw maps as direct texture references, so source filenames/suffixes
never matter for that step.

Once those four files exist in a folder, running **MAHS Setup** on it:

1. Finds `_Albedo`, `_MAHS`, `_Normal`, `_Height` by filename suffix
2. Sets the Normal map texture type to **Normal Map**
3. Sets `_MAHS` and `_Height` to Linear (sRGB off)
4. Backs up then expands `OPCDArray_Detail_Textures_MAHS_2048` — adds Albedo + MAHS (2 new slots)
5. Backs up then expands `OPCDArray_Normal_Textures_1024` — adds Normal (1 new slot)
6. Copies the template material to your source folder, named **OPCD MAHS - \<foldername\>**
7. Sets `_RedDetailIndex` to the new albedo slot, `_RedBaseBlend` to 0, and `_RedHeightTexture` to the height map

Every step registers an undo. If anything fails, all completed steps are automatically reversed. Use the **Emergency Restore** button in the window to manually roll back array changes from the most recent backup.

---

## Texture Workflows

### Option A — Quixel Bridge / Quixel Mixer

This is the standard workflow. Quixel exports files with suffixes the tool expects automatically.

**Required suffixes** (Quixel exports these by default):

| File suffix | Channel |
|---|---|
| `_Albedo` | Base color |
| `_AO` | Ambient Occlusion |
| `_Displacement` | Height / Displacement |
| `_Metalness` | Metalness |
| `_Normal` | Normal map |
| `_Roughness` | Roughness |

**Steps:**

1. Export your texture from Quixel Bridge or Quixel Mixer into your Unity project under `Assets/Resources/OPCD MAHS Resources/ExtraMaterials/<material-name>/`
2. Open **OPCD > MAHS Setup**, browse to that folder, and click **Run Setup**

If you are creating a custom texture from scratch and need to generate these maps, see **Option C** below.

---

### Option B — Fab.com (and similar texture sites)

Fab.com and other PBR texture libraries use slightly different suffix conventions. Do the following **before** running the tool:

**Required rename (one file):**

| Fab suffix | Rename to |
|---|---|
| `_BaseColor.jpg` | `_Albedo.jpg` |

**No rename needed:**

`_AO`, `_Displacement`, `_Normal`, `_Roughness` — these match already.

**Missing maps:**

- `_Metalness` — **not required.** If absent, the tool fills the channel with solid black (0% metalness), which is correct for non-metallic surfaces like leaves, wood, stone, fabric, etc.

**Ignored maps (leave them in the folder, they won't cause problems):**

`_Bump`, `_Cavity`, `_Gloss`, `_Specular`

**Steps:**

1. Download the texture set from Fab.com
2. In the downloaded folder, rename `<name>_BaseColor.jpg` → `<name>_Albedo.jpg`
3. Copy the folder into your Unity project under `Assets/Resources/OPCD MAHS Resources/ExtraMaterials/<material-name>/`
4. Let Unity finish importing
5. Open **OPCD > MAHS Setup**, browse to that folder, and click **Run Setup**

---

### Option C — Custom textures via Materialize (free)

Use this when you want to create a texture from any photo or image.

**Tools needed:**
- [Materialize](http://boundingboxsoftware.com/materialize/) — free, generates PBR maps from a single image
- [Tutorial video](https://www.youtube.com/watch?v=I1yO0UlLnC4)
- Quixel Mixer — packages the maps for export

**Steps:**

1. Find or photograph the surface you want. Save the image somewhere outside your Unity project for now.

2. **In Materialize:**
   1. Under **Diffuse Map**, click **O** and load your image
   2. Click **Create Heightmap** — start with defaults, adjust dials to taste
   3. Click **Create Normal**
   4. Click **Create Metalness**
   5. Click **Create AO**
   6. Click **Show Full Material** to preview
   7. Click **Tile Maps** — set to **2048 × 2048**, adjust Overlap X/Y until tiling looks good, then click **Set Maps**
   8. Set file format to **PNG**
   9. Click **Save Property Map** — save to your texture folder alongside the original image
   10. Save the Materialize project so you can re-adjust later

3. **In Quixel Mixer:**
   1. Start a new mix
   2. Go to **Library > Import Custom Asset**
   3. Map your Materialize outputs to the correct slots:
      - Diffuse → **Diffuse** and **Albedo**
      - Metalness → **Metalness**
      - AO → **AO**
      - Height → **Roughness** and **Displacement**
   4. Set Height value above 0 (otherwise the surface will appear flat)
   5. Click **Next** and fill in the asset details
   6. Select your new asset in the library
   7. Use the **Export** tab to export — follow the Masta Blasta tutorial for export settings
      - Make sure it exports into the correct folder inside your Unity project
      - **You do not need to invert the Alpha channel** with this workflow

4. In Unity, let the textures import, then follow **Option A** above.

---

### Option D — PolyHaven (and other raw-map sources)

PolyHaven delivers separate raw maps rather than a pre-packed MAHS file, so this goes through
**MAHSPackerWindow.cs** instead of a rename step.

**What to check on PolyHaven's download page:**

| Map | Take it? |
|---|---|
| Diffuse | Yes → Albedo slot |
| Normal (GL) | Yes → Normal slot. **Not** Normal (DX) — Unity's normal convention matches OpenGL. |
| AO | Yes → AO slot |
| Rough | Yes → Roughness slot |
| Displacement | Yes → Height slot |
| Metal | Only if the material is actually metallic (rare for organic/stone/wood/fabric) → Metalness slot. Leave unassigned otherwise — the packer treats a blank Metalness slot as fully non-metallic. |
| AO/Rough/Metal (combined) | **Skip this one.** The packer needs the three as separate images, not PolyHaven's packed ORM file. |
| Bump, Spec, Translucent | Not used by this pipeline — ignore. |
| Format | PNG/JPG both work; there's no need for EXR here. |

**Steps:**

1. Download the individual Diffuse / AO / Rough / Displacement / Normal (GL) maps (+ Metal if applicable) and import them anywhere in your Unity project.
2. Open **Tools > chickenandribs > MAHS Texture Packer**, drag each map into its matching slot, set a texture name and output folder, and click **Pack & Export**. This writes `_Albedo`, `_Normal`, `_Height`, and `_MAHS` into that folder.
3. Open **Tools > chickenandribs > MAHS Setup**, browse to that same folder, and click **Run Setup**.

A command-line equivalent exists at `decom_MAHS_Texture_Packer.py` (needs `pip install Pillow numpy`) if you'd rather batch this outside Unity — it auto-detects PolyHaven's filenames (`_diff`, `_nor_gl`, `_rough`, `_disp`, `_ao`, `_metal`) without any renaming.

---

## Troubleshooting

**"Red Detail Index" or "Red Base Blend" not applied**  
The tool logs a warning with the slot number so you can set the property manually. Confirm the property names match your shader by checking `RED_DETAIL_INDEX_PROP` / `RED_BASE_BLEND_PROP` at the top of `MAHSSetupWindow.cs`.

**Emergency Restore**  
Click the **Emergency Restore** button in the MAHS Setup window. This restores both texture arrays from the most recent backup taken before the last run.
