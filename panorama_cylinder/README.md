# Panorama Cylinder Tool

Arranges a set of separate panorama tile images (each a transparent-background
PNG, e.g. a Meshy AI mountain range render) around the inside of a cylinder —
one tile per equal angular slice — so they stitch into a seamless 360°
backdrop. Similar in spirit to `SkyDome` (an inside-out mesh meant to be
viewed from a camera near its center), but for a set of discrete tile images
instead of one wraparound equirectangular texture.

## Installation

Copy the whole `panorama_cylinder` folder's contents into your Unity
project's `Assets/` (the `.shader` file and `Scripts/` folder need to be
somewhere Unity compiles normally; `Editor/` needs to be — as always — inside
a folder literally named **Editor**).
Menu: **Tools > chickenandribs > Panorama Cylinder**.

## Usage

1. **Tools > chickenandribs > Panorama Cylinder > Create Spawner.** This
   drops a `Panorama Cylinder` object into the scene with a
   `PanoramaCylinderSpawner` component.
2. In the Inspector, add your tile images **in stitching order** under
   **Tiles** (tile 0 first, tile 1 next to it, and so on around the ring).
   There's no separate "how many segments" field — the number of tiles you
   add *is* the segment count, so degrees-per-segment (`360° / tile count`)
   adjusts automatically.
3. As soon as tiles are assigned, segment meshes appear live in the Scene
   view as children of the spawner. Every field below — Radius, Height,
   Bottom Y, Curve Subdivisions, Start Angle, and especially **Overlap
   Degrees** — rebuilds that preview live as you edit it, so you can drag a
   slider and watch the result instead of guessing values and re-baking to
   check.
4. **Overlap Degrees** is the seam-blend control: each tile's mesh extends
   that many degrees into its neighbor's territory on both sides, fading out
   (via vertex alpha) across that span, so a seam that doesn't quite line up
   softens into a blend instead of showing a hard edge. `0` = original
   hard-edged behavior.
5. Once it looks right, select the spawner and run **Tools > chickenandribs >
   Panorama Cylinder > Bake Selected**. This saves one mesh + one material
   per tile under `Assets/PanoramaCylinder/`, plus the whole ring as a
   reusable prefab — this is what actually ships; the spawner's own live
   preview children are editor-only (never saved with the scene, never
   present in a build).

## Notes

- Each segment's material uses the custom **Custom/PanoramaCylinderBlend**
  shader (ships in this package as `PanoramaCylinderBlend.shader`) rather
  than a built-in one — it clamps UVs at each tile's own edge (so the overlap
  region stretches that tile's edge pixel instead of wrapping) and multiplies
  sampled alpha by the baked-in vertex-alpha fade. A plain `Unlit/Transparent`
  can't do either of those, which is why this tool ships its own shader.
- The mesh uses the same "inside-out" technique as `SkyDome` (inverted
  normals + reversed winding) so it renders correctly for a camera positioned
  inside the ring under Unity's default back-face culling.
- This tool doesn't add any rotation/animation (unlike `SkyDome`'s slow cloud
  drift) — the ring is static.
- If the seam still looks off at very high Overlap Degrees, that usually
  means the source tiles' horizon heights or terrain colors don't match at
  all (not just a small alignment slip) — no amount of UV blending fixes a
  large color/height mismatch between two independently-rendered tiles;
  that needs a source-image fix instead.
