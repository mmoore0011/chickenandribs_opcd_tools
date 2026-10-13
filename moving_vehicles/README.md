# Moving Vehicles — Unity Package

## Description

Low-poly background traffic for Unity **2018.2.8f1** and later. No
external assets, no animations beyond baked movement, no physics —
vehicle meshes are generated procedurally from code, and movement is
baked into standard Unity Animation clips so nothing depends on custom
scripts at GSPro runtime.

**What's included:**

| File | Purpose |
|---|---|
| `Scripts/VehiclePath.cs` | Defines the road via child waypoints |
| `Scripts/VehicleSpawner.cs` | Creates, manages, and destroys vehicles |
| `Scripts/VehicleController.cs` | Moves each vehicle along the path |
| `Scripts/VehicleBuilder.cs` | Procedurally builds low-poly vehicle meshes |
| `Scripts/BakedVehicleTag.cs` | Lightweight tag used by the baker |
| `Scripts/WaypointStop.cs` | Optional per-waypoint pause duration |
| `Editor/VehiclePathEditor.cs` | Custom inspector + scene handles for VehiclePath |
| `Editor/VehicleBaker.cs` | One-click bake tool for GSPro / asset-bundle export |

**Vehicle types** (all procedurally generated, no imported meshes):

| Type | Approx. size | Notes |
|---|---|---|
| Sedan | 4.4 m × 1.8 m × 1.5 m | |
| Hatchback / Crossover | 4.0 m × 1.8 m × 1.7 m | |
| Pickup Truck | 5.5 m × 1.9 m × 1.8 m | |
| Tractor/Trailer (Semi) | 16 m × 2.4 m × 3.7 m | |
| Food Truck (BBQ) | 5.8 m × 2.1 m × 3.6 m | Awning + sign on passenger side |

A ready-to-use prefab for the food truck can be created from
**Tools → chickenandribs → Moving Vehicles → Create Food Truck Prefab**,
saved to `Assets/MovingVehicles/Prefabs/FoodTruck.prefab`, which can also
be dropped into any VehicleSpawner's **Custom Prefabs** list.

## Installation

**Option A — copy into your project**: copy `Assets/MovingVehicles` into
your Unity project's `Assets` folder; Unity compiles it automatically.

**Option B — export as `.unitypackage`**: right-click
`Assets/MovingVehicles` → **Export Package…** → keep everything checked →
Export. Import elsewhere via **Assets → Import Package → Custom Package…**

## Step-by-Step Instructions

### 1 — Create the path

1. **GameObject → Create Empty** → rename it `RoadPath`.
2. **Add Component → Vehicle Path**.
3. Use the custom Inspector's **+ Add Waypoint** / **Remove Last** / **Select**
   buttons, and drag the orange position handles in the Scene view to place
   each waypoint.

> Put **Waypoint_00** and the last waypoint somewhere **not visible** to
> the player camera (behind a building, at the edge of render distance).
> The green/red spheres show the actual spawn/despawn points — adjust
> *Spawn Lead Distance* / *Despawn Lead Distance* to push them further if
> needed. Add a **WaypointStop** component to any waypoint to make
> vehicles pause there for a set duration before continuing.

### 2 — Add the spawner

1. **GameObject → Create Empty** → rename it `TrafficSpawner`.
2. **Add Component → Vehicle Spawner**.
3. Drag `RoadPath` into the **Vehicle Path** field.
4. (Optional) Create an empty `Vehicles` GameObject and drag it into
   **Vehicle Parent** to keep spawned vehicles tidy in the Hierarchy.

### 3 — Tune the settings

| VehicleSpawner field | Default | Effect |
|---|---|---|
| Max Vehicles | 6 | How many cars on the road at once |
| Min / Max Spawn Interval | 2 / 6 s | Random gap between each new spawn |
| Min / Max Speed | 8 / 14 | ~30–50 km/h (world units = metres) |
| Color Pool | 8 colors | Body colors assigned at random each spawn |
| Sedan / Hatchback / Pickup / Tractor / Food Truck Weight | 3/3/2/1/1 | Relative spawn frequency (0 disables a type) |

| VehiclePath field | Default | Effect |
|---|---|---|
| Spawn Lead Distance | 6 m | How far before Waypoint_00 vehicles appear |
| Despawn Lead Distance | 6 m | How far after the last waypoint vehicles disappear |

### 4 — Hit Play

Vehicles appear from the spawn point and drive to the despawn point.

### Multiple lanes / roads

Add more `VehicleSpawner` + `VehiclePath` pairs, one per lane — each is
fully independent.

### Changing colors or density at runtime

```csharp
var spawner = FindObjectOfType<MovingVehicles.VehicleSpawner>();
spawner.colorPool.colors = new Color[] { Color.red, Color.blue };

spawner.maxVehicles      = 10;
spawner.minSpawnInterval = 1f;
spawner.maxSpawnInterval = 3f;
```

### Exporting to GSPro (bake workflow)

GSPro's course format is an asset bundle, which cannot include custom
MonoBehaviour scripts and only serializes objects that already exist as
saved project assets before the build — so the runtime spawner alone
won't work in GSPro. Bake everything into real, serialized scene objects
first:

1. Set up your path and spawner as above, verify it in Play mode.
2. **Stop Play mode** (baking must run in Edit mode).
3. **Tools → chickenandribs → Moving Vehicles → Bake All Vehicles in Scene** —
   meshes/materials/AnimationClips are saved under `Assets/MovingVehicles/Baked/`,
   and vehicles become real GameObjects driven by Unity's built-in
   Animation component, with zero custom scripts needed at GSPro runtime.
4. Save the scene, then export your course as normal.

To iterate: **Tools → chickenandribs → Moving Vehicles → Clear Baked Vehicles**
removes all baked vehicles and re-enables the live spawner, then adjust
settings and bake again.

## Troubleshooting

- **No wheel rotation** — wheels are static low-poly cylinders; this is
  intentional for background-only use at a distance.
- **No collision** — vehicles can clip through each other. For
  background traffic viewed at range this is usually unnoticeable.
- **No LOD** — every vehicle uses the same mesh regardless of distance,
  but poly counts are already very low (~100–350 tris/vehicle).
- **A vehicle does a 360° spin at a sharp-turn waypoint** — this is a
  known Unity quirk (component-wise quaternion curve interpolation taking
  the "long way around" between two keyframes) and should already be
  guarded against in `VehicleBaker.cs`. If you see it anyway, check the
  version marker at the top of that file and update if it's older than
  2026-10-01.
- Custom scripts (VehicleSpawner/VehicleController) only drive vehicles
  in the Editor/Play mode — after baking, those scripts are disabled and
  movement comes entirely from the baked Animation clips instead. This is
  expected, not a bug.

## Feature Requests / Bug Reports

Please open an issue in this repository.

## Video

- [Moving Vehicles — overview](https://www.youtube.com/watch?v=UauRl9eOURI)
