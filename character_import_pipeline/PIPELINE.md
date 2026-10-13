# Character Pipeline: Blender → Unity → Timeline

The full, validated procedure for getting a rigged character from Blender into
a Unity Timeline, for the FTR film studio project. Follow this in order —
every step exists because skipping it caused a specific, previously-diagnosed
failure (noted inline).

## One-time setup

1. Install the Blender add-on: Blender → Edit → Preferences → Add-ons →
   Install → select `mm_tools/recipes/character_fbx_exporter.py` → enable it.
   A new **FTR Export** tab appears in the 3D viewport's sidebar (press `N`).
2. Import `character_import_pipeline.unitypackage` into the Unity project
   (Assets → Import Package → Custom Package). This is a silent
   `AssetPostprocessor` — there's no menu or window. From this point on it
   runs automatically on every FBX import under `characters/<name>/`.

## Per-character: one-time rig fix

Skip this if the character's Dimensions already read correctly in Blender
(check the N-panel → Item tab → Dimensions with the mesh selected).

1. Select the Armature **and** the mesh together.
2. Scale them (`S`, watch the Dimensions readout) until the character reads
   roughly 1.7–1.9m tall.
3. **Object → Apply → Scale** on both. This bakes the correction into the
   actual mesh/bone data instead of leaving it as an object-level multiplier
   — *(skipping this is why Bram randomly became "ant-sized" mid-session:
   a leftover Auto-Key kept re-animating the multiplier instead of it being
   a fixed, applied value)*.
4. If the character also needs a rotation fix (rare — check by exporting a
   quick test and looking at it in Unity), rotate the same pair in Object
   Mode and Apply Rotation too.
5. Make sure nothing on the Armature's own Scale/Rotation/Location channels
   is animated — right-click each Transform field and confirm it offers
   **Insert Keyframe**, not **Clear Keyframes**. If it offers Clear, use it;
   an animated Object-level transform will fight any value you set by hand.

## Exporting

Open the **FTR Export** panel (`N` → FTR Export tab).

**Base Model** (once per character, or whenever the rig/mesh changes):
1. Mode: **Base Model**.
2. Export To: `.../characters/<name>/<name>.fbx`.
3. Click **Export**.

**Animation Clip** (once per clip — idle, walk, sit, everything):
1. Set the Timeline's **Start**/**End** fields to the exact frame range for
   this clip.
2. Mode: **Animation Clip**.
3. Export To: `.../characters/<name>/animations/<clip_name>.fbx`.
4. Click **Export**.

Both modes use fixed, correct transform settings — there's no dialog to
misconfigure. Animation Clip exports include the mesh too, even though only
the armature moves — these are lean Blender exports (under 1MB either way),
and including it means every file can reliably auto-detect its own Humanoid
avatar on import. No particular import order is required between files.

## Bringing files into Unity

1. Copy the `.fbx` into the project under `characters/<name>/...`, matching
   the export path above.
2. Let Unity import it. `character_import_pipeline` automatically:
   - sets Materials to External (so texture wiring survives future reimports
     — "Use Embedded Materials," Unity's default, discards it every time),
   - sets Animation Type to Humanoid (each file creates its own avatar —
     reliable here because every export includes mesh; see note below),
   - finds sibling texture files (walking up toward the character's root
     folder if needed) and wires them into the extracted material by
     filename keyword (`metallic`, `normal`, `emission`, `occlusion`; the
     un-suffixed file is treated as Albedo).
3. **First time only per character:** the extracted material won't have
   textures wired yet if none exist beside the base model on its very first
   import (chicken-and-egg on frame one). If it comes in untextured, drag
   the character's texture files onto the extracted `.mat` asset by hand
   once — every file after that, for this character, wires up automatically.
4. **First time only per character, separately:** even with correct,
   standard bone names (`Hips`, `LeftUpLeg`, `Spine01`, etc. — confirmed
   fine by re-opening an exported file in Blender), Unity's automatic
   Humanoid bone-mapping at import time can come back with every single
   slot empty ("Required human bone 'Hips' not found"), even though the
   exact same detection logic works instantly when triggered by hand. Fix:
   select the file → Rig tab → **Configure...** → **Mapping** dropdown →
   **Automap** → confirm every slot filled in green → **Apply** → **Done**.
   This has only been needed on the *first* file imported for a character —
   once Unity has a successful mapping for a given set of bone names, other
   files sharing those same names (i.e. every other animation clip for that
   same character) pick it up automatically on import, no repeat needed.
   If a later file for the same character *does* still show the error, redo
   this same Automap pass on that one file.

## Building the Timeline

1. Drag the character's Base Model into the scene, positioned where the shot
   needs it.
2. Confirm its **Animator** component has the shared Avatar set and
   **Controller** left as **None** — Timeline drives it directly, no
   Animator Controller/state machine needed.
3. Create (or open) a Timeline asset with a **Director** component (on this
   object or a separate one — doesn't matter which).
4. Add an **Animation Track**, and bind it: click the binding slot, use the
   **Scene** tab (not Assets) in the picker, and select the character
   instance in your scene.
5. Expand each animation clip FBX in the Project window and drag its clip
   sub-asset (note: Blender's default scene name, "Scene," often ends up as
   the clip's name inside the FBX — that's the actual animation clip, not a
   mistake) onto the Animation Track.
6. Scrub the Timeline to confirm it plays correctly before moving on to the
   next clip.

## Troubleshooting quick reference

| Symptom | Cause | Fix |
|---|---|---|
| Character huge/tiny, or ant-sized after looking correct | Object-level Scale animated (Auto Key caught it) or export used "FBX Units Scale" instead of "All Local" | Clear the Scale keyframes and set 1.0 by hand; re-export with this add-on, which hardcodes "All Local" |
| Character lying down / tipped over | Raw rig import issue, specific to non-Blender-exported sources — re-exporting through this pipeline should not reproduce it | Re-export via this tool; if still wrong, rotate + Apply Rotation once per the one-time rig fix above |
| Material shows grey/white, no texture | Material extracted but no sibling texture found yet, or reimported after a stale `.meta` cached the old embedded-material link | Drag textures onto the extracted `.mat` by hand once; check External material location is set |
| "Required human bone 'Hips' not found" | Unity's *automatic* Humanoid bone-mapping at import time is unreliable even with correct bone names/mesh present — confirmed the bone data itself is fine (standard names like `Hips`/`LeftUpLeg`, correct hierarchy) since manually triggering the same detection succeeds instantly | Rig tab → Configure... → Mapping → Automap → Apply → Done. Needed once per character (first file only); other files sharing the same bone names pick up the mapping automatically after that |
| Model disappears when a clip is added to Timeline, but looks fine standing still | Almost always actually *huge and distorted*, not invisible — camera ends up inside the mesh | Check the export used "All Local" scaling, not "FBX Units Scale" |
