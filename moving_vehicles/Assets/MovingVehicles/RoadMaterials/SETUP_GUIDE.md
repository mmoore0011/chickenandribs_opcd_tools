# Unity Highway Markings — Setup Guide

## Files Included
| File | Purpose |
|------|---------|
| `highway_markings.png` | Road marking texture (512×1024 RGBA, transparent bg) |
| `HighwayMarkings.shader` | Custom Unity surface shader |

---

## Texture Specs
- **Width represents**: 11 meters of road (full lane width)
- **Height represents**: 12 meters along the road
- **Left line**: Solid yellow at 0.5m inset — USA centerline / passing zone marker
- **Center**: White dashes (3m on / 9m gap) — lane divider
- **Right line**: Solid white at 0.5m inset — road edge marker
- **Background**: Transparent (alpha=0) — road surface shows through

---

## Step-by-Step Unity Setup

### 1. Import Files
1. Copy `highway_markings.png` and `HighwayMarkings.shader` into your `Assets/` folder.
2. Select `highway_markings.png` in the Project window.
3. In the Inspector, set:
   - **Texture Type**: Default
   - **Alpha Source**: Input Texture Alpha
   - **Alpha is Transparency**: ✅ Checked
   - **Wrap Mode**: Repeat
   - **Filter Mode**: Bilinear or Trilinear
   - Click **Apply**

### 2. Create the Material
1. Right-click in Project → **Create → Material**
2. Name it `HighwayMarkings_Mat`
3. In the Inspector, click the **Shader** dropdown → select `Custom/HighwayMarkings`

### 3. Assign Textures
- **Road Surface Texture**: Assign your existing asphalt/road texture (the grey one from your scene)
- **Markings Texture**: Assign `highway_markings.png`
- **Normal Map** (optional): Assign a road normal map if you have one

### 4. Set Tiling

This is the critical step for physical accuracy.

**Markings Texture tiling:**
- X (across road): `1.0` (texture already covers full 11m width)
- Y (along road): Set to match your mesh length ÷ 12
  - Example: mesh is 60m long → Y tiling = `60 / 12 = 5.0`

**Road Surface Texture tiling:**
- Set independently based on your asphalt texture's natural scale
- Typical: X = `2.0–4.0`, Y = `8.0–20.0` (asphalt tiles more finely)

### 5. Apply to Mesh
- Drag `HighwayMarkings_Mat` onto your road plane/mesh in the scene

---

## UV Mapping Requirements
Your road mesh must have UVs where:
- **U = 0** is the **left edge** of the road
- **U = 1** is the **right edge** of the road
- **V** runs along the road direction

If your mesh was created procedurally (e.g., via a spline tool), ensure UV width normalization is enabled.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Lines appear squished/stretched | Adjust Y tiling on Markings Texture |
| Yellow line on wrong side | Rotate mesh 180° around Y axis, or flip texture V |
| Lines too faint | Set Markings Tint alpha to 1.0 |
| Road surface not visible | Check Road Surface Texture is assigned |
| Shader not appearing in list | Check shader file has no compile errors (Window → Analysis → Shader) |

---

## Using URP or HDRP?

The included shader is **Built-in Render Pipeline** only.

**For URP**: Create a Shader Graph
1. Right-click → Create → Shader Graph → URP → Lit Shader Graph
2. Add two `Sample Texture 2D` nodes (road + markings)
3. Use a `Lerp` node driven by the markings texture's **Alpha** channel
4. Connect to the **Base Color** of the Master Stack

**For HDRP**: Same approach using an HDRP Lit Shader Graph.

---

## Line Dimensions Reference (USA MUTCD Standard)
| Feature | Real World | In Texture |
|---------|-----------|------------|
| Road width | 11.0 m | Full width (U: 0→1) |
| Yellow line inset | 0.5 m from left | U ≈ 0.045 |
| White line inset | 0.5 m from right | U ≈ 0.955 |
| Line width | ~0.15 m (6 in) | ~6px |
| Dash length | 3.0 m | 139px |
| Gap length | 9.0 m | 418px |
| Cycle length | 12.0 m | Full texture height |
