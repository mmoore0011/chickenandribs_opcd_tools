# Army Scatter Tool

Batch-places a set number of randomly-mixed prefabs inside a defined area, spaced apart to avoid overlap, raycast down onto the ground so nothing floats or clips, with one shared facing rotation for the whole group. Built for scattering a "messy" battlefield crowd without hand-placing each unit or having them collide into each other.

This is a one-click batch generator, not a click-drag Scene tool like `rock_scatter` — set the options, click **Place All**, and it generates the whole batch at once.

## Installation

Copy `ArmyScatterWindow.cs` into any folder named **Editor** inside your Unity project's `Assets/` folder.
Open via **Tools > chickenandribs > Army Scatter**.

## Usage

1. Under **Prefabs**, add each unit type you want mixed into the crowd. One is picked at random per placement.
2. Under **Area**, either drag in an existing Transform as **Area Center**, or click **Create** to drop a new empty GameObject — position it wherever you want the crowd centered. Set **Size (X / Z)** for the width/depth of the placement area. A wireframe box previews the area live in the Scene view while this window is open.
3. Optionally check **Organized Formation (grid)** — see below.
4. Set **Count** (max 100), the spacing field (**Min Spacing** in scattered mode, **Row/Column Spacing** in grid mode), and **Facing (Y Rotation)** — the single direction every placed unit itself faces.
5. Optionally assign a **Parent** — if left blank, a `Scattered Army` GameObject is created automatically.
6. Click **Place All**.

Each placed unit is a normal Undo entry, and the whole batch is grouped under the parent for easy selection/deletion if you want to reroll.

## Organized Formation

Checking **Organized Formation (grid)** switches from random scattering to neat, evenly-spaced rows and columns — for units standing at attention rather than a battle scatter. The grid is sized automatically from **Count** and **Row/Column Spacing** (roughly square), centered on the Area Center, and still raycasts each slot down to the actual ground height. **Area Center's rotation** determines which way the rows/columns run (independent of **Facing**, which is which way each unit faces) — rotate the Area Center object to angle the whole formation.

## Notes

- Needs an actual collider on the ground under the area — placement raycasts straight down from **Raycast Start Height** above the area center; increase that value if your terrain has tall features that might otherwise be missed or hit first.
- If the Console logs placing fewer units than requested, the area/grid is too small/dense for the current spacing — widen the area, reduce the count, or reduce spacing.
- Facing is a single Y-axis rotation for the entire group — this tool doesn't randomize each unit's orientation individually, since the goal is a coherent-looking formation/crowd rather than scattered debris (see `rock_scatter` for per-instance random spin).
- If a placed unit's prefab uses an Animator Controller with sibling "(offset N)" variants next to it (e.g. ones created by `BatchSwordSetup`), one is picked at random per unit so many instances of the same looping animation don't all move in lockstep.
