# Rock Scatter Tool

Click (or click-drag) on any collider in the Scene view to place a random rock prefab there, oriented so its flat base rests against the surface — including on cliff faces and slopes, not just flat ground.

## Installation

Copy `RockScatterWindow.cs` into any folder named **Editor** inside your Unity project's `Assets/` folder.
Open via **Tools > chickenandribs > Rock Scatter**.

## Usage

1. Under **Prefabs**, click **Add Prefab Slot** and drag in each rock prefab you want to scatter (e.g. `FTR_snow_rough_rock1` through `4`). Multiple prefabs get picked at random per placement.
2. Optionally assign a **Parent** transform to keep scattered rocks organized. If left blank, a `Scattered Rocks` GameObject is created automatically.
3. Set **Flat-Face Away Axis** if needed — see Fields below.
4. Adjust Scale Range / Drag Spacing / Embed Depth / Spin Mode to taste.
5. Click **Enable Placement**.
6. In the Scene view, click a spot on any collider to place one rock, or click-drag along a surface to lay down a line of rocks spaced apart by **Drag Spacing**.
7. Click **Enable Placement** again to turn it off (normal Scene view clicking/selection resumes).

Each placement is a normal Undo step (Ctrl+Z), so overcorrecting a stroke is cheap.

## Fields

- **Prefabs** — one or more rock models. One is picked at random per placement.
- **Parent** — optional Transform to group placed rocks under. Defaults to an auto-created `Scattered Rocks` GameObject.
- **Flat-Face Away Axis** — which local axis of the prefab points away from its flat base; this is the axis that gets aligned to the surface normal at the click point. Defaults to +Y. If rocks land tipped on their side instead of lying flat, try the other options — Meshy AI exports in particular don't reliably use +Y for this. Once you find the right axis for one prefab from a given export batch, it should hold for the rest of that batch too.
- **Scale Range** — a *multiplier* on the prefab's own existing scale, not an absolute size. Each placement gets a random value in this range applied on top of whatever scale the prefab already has (e.g. a prefab already scaled to 100/100/100 with a range of 0.85–2.46 lands somewhere between 85 and 246).
- **Drag Spacing (m)** — minimum distance between rocks placed during a single click-drag stroke.
- **Embed Depth (m)** — how far to push each rock into the surface along the hit normal, to help hide the flat cut edge of its base.
- **Spin Mode** — controls the rock's twist around the surface normal (independent of the flat-face alignment):
  - **Random** — a new random twist per rock, so repeats of the same prefab don't look identical.
  - **Fixed** — every rock gets the same **Spin Angle**, shown as both a numeric slider and a compass-style dial (0° up, clockwise) — drag the dial or type a value, both stay in sync. Useful for planting a run of rocks at one consistent angle and nudging it as you go, without needing placement off.
  - **Off** — no added twist beyond the flat-face alignment itself.

## Notes

- Orientation is computed from the raycast hit normal, not world-up — this is what makes rocks sit flush on a vertical cliff face instead of floating with their flat base pointed sideways.
- Needs an actual collider (e.g. Mesh Collider) on the surface you're clicking — it won't place on render-only geometry.
