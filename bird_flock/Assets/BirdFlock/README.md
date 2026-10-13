# Bird Flock — Unity Package

Procedural background bird flocks for Unity **2018.2.8f1** and later.  
No external assets, no animations, no physics — everything is generated from code.

---

## What's included

| File | Purpose |
|---|---|
| `Scripts/BirdPath.cs` | Defines the flight path via child waypoints |
| `Scripts/BirdSpawner.cs` | Creates, manages, and destroys flocks |
| `Scripts/BirdController.cs` | Moves each flock along the path |
| `Scripts/BirdBuilder.cs` | Procedurally builds low-poly birds and arranges them in formation |
| `Editor/BirdPathEditor.cs` | Custom inspector + scene handles for BirdPath |
| `Editor/BirdSpawnerEditor.cs` | Custom inspector for BirdSpawner |

### Flight patterns
| Pattern | Description |
|---|---|
| V Formation | Asymmetric V — one wing longer than the other, 1–2 stragglers behind the pack |

### Bird model (procedurally generated, no imported meshes)
| Part | Description |
|---|---|
| Body | Slender elongated box (~0.46 m long) |
| Wings | Two flat boxes with ~10° upward dihedral, ~1.1 m total wingspan |
| Head | Small box at the nose |
| Tail | Thin box at the rear |

---

## Installation

### Option A – Copy into your project
1. Copy the `Assets/BirdFlock` folder into your Unity project's `Assets` folder.
2. Unity will compile the scripts automatically.

### Option B – Export as `.unitypackage`
1. In Unity, right-click `Assets/BirdFlock` → **Export Package…**
2. Keep all items checked → **Export**.
3. To import in another project: **Assets → Import Package → Custom Package…**

---

## Scene Setup (step by step)

### 1 — Create the flight path

1. **GameObject → Create Empty** → rename it `BirdFlightPath`.
2. **Add Component → Bird Path**.
3. Use the custom Inspector buttons:
   - **+ Add Waypoint** — adds a child waypoint after the last one.
   - **Remove Last** — removes the last waypoint.
   - **Select** — selects a waypoint so you can move it in the Scene view.
4. Drag the orange position handles to place each waypoint **at the altitude you want the birds to fly**.

> **Tip:** Place `Waypoint_00` and the last waypoint behind terrain or at the edge of render  
> distance so birds appear and disappear naturally off-screen.  
> Use the **Snap All Waypoints to Terrain** button to align waypoints to the terrain surface,  
> then set **Altitude Above Surface** (default 20 m) to lift them to a realistic flying height.

### 2 — Add the spawner

1. **GameObject → Create Empty** → rename it `BirdSpawner`.
2. **Add Component → Bird Spawner**.
3. Drag your `BirdFlightPath` object into the **Bird Path** field.
4. (Optional) Create an empty GameObject named `Flocks` and drag it into **Flock Parent**
   to keep spawned flocks tidy in the Hierarchy.

### 3 — Tune the settings

**BirdSpawner fields:**

| Field | Default | Effect |
|---|---|---|
| Flight Pattern | V Formation | Formation shape — only V Formation is currently available |
| Number of Birds | 14 | Birds per flock (3–50) |
| Min Interval | 240 s (4 min) | Minimum wait before next flock appears |
| Max Interval | 720 s (12 min) | Maximum wait before next flock appears |
| Min / Max Speed | 8 / 14 m/s | ~29–50 km/h — typical geese cruising speed |
| Flock Parent | (none) | Optional parent for Hierarchy organization |

> The Inspector shows a human-readable hint below each interval field,  
> e.g. `(4.0 minutes)` or `(1.00 hour)`, so no mental conversion is needed.

**BirdPath fields:**

| Field | Default | Effect |
|---|---|---|
| Spawn Lead Distance | 10 m | How far before Waypoint_00 flocks appear |
| Despawn Lead Distance | 10 m | How far after the last waypoint flocks disappear |

### 4 — Hit Play

Flocks will begin appearing at the spawn point and fly to the despawn point,  
then silently remove themselves from the scene.

---

## Making multiple flight paths

Add more `BirdSpawner + BirdPath` pairs — one per path.  
Each spawner is fully independent and uses its own random timing.

---

## V Formation details

Each flock is randomly generated within these rules:

- **Leader** — one bird at the apex of the V
- **Wings** — one wing gets 35–52% of the formation birds (the split is re-rolled each spawn),
  making the V naturally asymmetric on every pass
- **Stragglers** — 1 bird (small flocks) or 1–2 birds (8+ birds) placed 2–4.5 m behind
  the deepest wing bird, loosely off-center
- **Jitter** — every bird position has a small random offset (±0.45 m horizontal,
  ±0.2 m vertical) so the formation looks natural rather than mechanical

---

## Notes & limitations

- **No wing animation** — wings are static low-poly geometry. This is intentional for
  background-only use.
- **No collision** — flocks pass through each other and through geometry. At altitude
  and distance this is unnoticeable.
- **No LOD** — all birds use the same mesh regardless of distance. The poly count is
  very low (~30 tris per bird) so this is rarely an issue.
- **GSPro bake** — use **Menu → Tools → chickenandribs → Bird Flock → Bake All Flocks in Scene**
  before exporting. The baker saves bird meshes/materials as project assets and converts
  flock movement into AnimationClips (Unity's built-in Animation component — no custom
  scripts needed at GSPro runtime). Two flocks are baked per spawner with staggered timing
  so a new flock appears roughly every `(travelTime + avgInterval) / 2` seconds.
  Use **Clear Baked Flocks** to remove baked results and re-enable the spawner for editing.
- **Unity 2018.2 compatible** — no APIs introduced after 2018.2 are used.
