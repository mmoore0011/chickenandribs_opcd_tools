# Mesh Gap Detector

## Description

A Blender addon for OPCD course designers that finds gaps between meshes —
boundary vertices that aren't actually connected to any adjacent mesh. This
catches both gaps *between* separate meshes and interior holes *within* a
single mesh (e.g. from accidental reshaping), and can snap flagged
vertices back together automatically.

**⚠️ Please make a backup of your project before using this tool.**

## Installation

1. Blender → Edit → Preferences → Add-ons → Install → select
   `mesh_gap_detector.py` → enable it.
2. A new **Gap Tools** tab appears in the 3D viewport's sidebar (press `N`).

## Step-by-Step Instructions

1. Open the **Gap Tools** tab in the N-panel.
2. Click **Detect Mesh Gaps in Entire Project** (Object Mode) to scan every
   `Spline`-prefixed mesh, or **Detect Mesh Gaps in Selected Mesh** to
   check just the active object.
3. Results list each flagged vertex with three buttons:
   - 🔍 **Zoom/Inspect** — jumps into Edit Mode on that object with the gap
     vert(s) selected.
   - 🔗 **Snap** — snaps the flagged vertex to the nearest vertex on any
     other Spline mesh within 1m.
   - 🖥 **Debug** — prints detailed info to the System Console (Window →
     Toggle System Console).
4. For a messier fix: select a rough group of vertices in Edit Mode
   (mixing gap verts with already-connected ones is fine) and click
   **Fix Gaps in Selected Vert(s)** — it filters to boundary verts, skips
   anything already properly connected, and snaps only the genuinely
   unconnected ones.

## Troubleshooting

- **A result looks like a false positive right at the edge of the whole
  project** — the tool already excludes vertices within 6m of the Terrain
  object's boundary, but this margin is tuned for a typical OPCD project
  and may need adjustment for unusual layouts.
- **Water meshes show up as gaps** — meshes using the WaterPlane material
  are skipped by design, but `Water_Base_Lake`-named meshes aren't
  automatically excluded and may show near-miss false positives since
  they intentionally aren't stitched to surrounding meshes.
- **A vertex sits exactly ON an edge of another mesh, not on a vertex** —
  this is flagged correctly (it's a real topological gap), and the Snap
  button resolves it.
- **Zoom/Inspect jumps to an odd view on a single flagged vertex** — a
  known rough edge in some Blender versions; try the Debug button for
  more detail on exactly where the vertex is.

## Feature Requests / Bug Reports

Please open an issue in this repository.

## Video

- [Mesh Gap Detector — overview](https://www.youtube.com/watch?v=nrHMX34BscU)
- [Mesh Gap Detector — in depth](https://www.youtube.com/watch?v=lMz-ndHkKDY)
