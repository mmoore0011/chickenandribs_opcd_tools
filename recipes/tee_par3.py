"""
Tee Box Vertex Paint Recipe - Par 3
====================================
Paints a tee box mesh with realistic wear for a par 3:
  Red   = base tee surface (dominant)
  Black = dark worn spots
  Blue  = dry / sun-bleached patches
  Green = divot scars (par 3 only)

Run from Blender's Scripting editor with the tee mesh selected.
Works with Blender 3.x and 4.x.
"""

import bpy
import random
import math
from mathutils import Vector

# ── Tuning ────────────────────────────────────────────────────────────────────
SEED = None          # int for reproducible runs, None for random each time

# Blob counts  (number of distinct patches per effect)
N_DARK_PATCHES  = 6
N_DRY_PATCHES   = 8
N_DIVOT_PATCHES = 12   # par 3 has more divots

# Blob size expressed as a fraction of the mesh's longest diagonal
DARK_RADIUS_FRAC  = 0.10   # 10 % of bbox diagonal
DRY_RADIUS_FRAC   = 0.12
DIVOT_RADIUS_FRAC = 0.04   # divots are small, concentrated

# Peak strength of each effect (how much it pushes away from pure red)
DARK_STRENGTH  = 0.20   # subtracts from all channels → dark
DRY_STRENGTH   = 0.18   # adds blue
DIVOT_STRENGTH = 0.14   # adds green

# Falloff sharpness (higher = tighter blob edge)
FALLOFF = 3.5
# ──────────────────────────────────────────────────────────────────────────────


def _ensure_color_layer(mesh, name="Col"):
    """Return an active vertex-color layer, creating one if needed."""
    if hasattr(mesh, "color_attributes"):          # Blender 4.x
        if name not in mesh.color_attributes:
            mesh.color_attributes.new(name, "FLOAT_COLOR", "CORNER")
        attr = mesh.color_attributes[name]
        mesh.color_attributes.active_color = attr
        return attr.data, "color"
    else:                                           # Blender 3.x
        if not mesh.vertex_colors:
            mesh.vertex_colors.new(name=name)
        layer = mesh.vertex_colors[name] if name in mesh.vertex_colors \
                else mesh.vertex_colors.active
        return layer.data, "color"


def _mesh_diagonal(obj):
    """Approximate bounding-box diagonal in local space."""
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
    hi = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
    return (hi - lo).length


def _build_loop_vertex_map(mesh):
    """Map loop index → vertex world position."""
    loop_pos = []
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            loop_pos.append((li, mesh.vertices[vi].co.copy()))
    return loop_pos


def _gaussian(dist, radius):
    """Smooth Gaussian falloff; 0..1 where 1 = at centre."""
    if dist >= radius:
        return 0.0
    t = dist / radius
    return math.exp(-FALLOFF * t * t)


def _random_surface_point(mesh, obj):
    """Pick a random polygon centre (world space)."""
    poly = random.choice(mesh.polygons)
    local_centre = poly.center
    return obj.matrix_world @ local_centre


def paint_tee_par3():
    obj = bpy.context.active_object
    if obj is None or obj.type != "MESH":
        raise RuntimeError("Select a mesh object before running.")

    mesh = obj.data
    if SEED is not None:
        random.seed(SEED)

    color_data, color_attr = _ensure_color_layer(mesh)
    diagonal = _mesh_diagonal(obj)
    loop_map  = _build_loop_vertex_map(mesh)

    # ── Step 1: flood-fill base red ──────────────────────────────────────────
    for li, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    # ── Helper: accumulate delta arrays ──────────────────────────────────────
    n_loops = len(mesh.loops)
    dr = [0.0] * n_loops   # delta red   (negative → darker)
    dg = [0.0] * n_loops   # delta green (divots)
    db = [0.0] * n_loops   # delta blue  (dry)

    def add_blobs(count, radius_frac, strength, channel):
        radius = diagonal * radius_frac
        for _ in range(count):
            centre = _random_surface_point(mesh, obj)
            world_centre = centre
            for li, local_co in loop_map:
                world_co = obj.matrix_world @ local_co
                dist = (world_co - world_centre).length
                w = _gaussian(dist, radius) * strength * random.uniform(0.4, 1.0)
                if channel == "dark":
                    dr[li] = max(dr[li], -w)
                    dg[li] = max(dg[li], -w * 0.8)
                    db[li] = max(db[li], -w * 0.8)
                elif channel == "blue":
                    db[li] = max(db[li], w)
                    dr[li] = min(dr[li], -w * 0.3)
                elif channel == "green":
                    dg[li] = max(dg[li], w)
                    dr[li] = min(dr[li], -w * 0.5)

    add_blobs(N_DARK_PATCHES,  DARK_RADIUS_FRAC,  DARK_STRENGTH,  "dark")
    add_blobs(N_DRY_PATCHES,   DRY_RADIUS_FRAC,   DRY_STRENGTH,   "blue")
    add_blobs(N_DIVOT_PATCHES, DIVOT_RADIUS_FRAC, DIVOT_STRENGTH, "green")

    # ── Step 2: apply accumulated deltas ─────────────────────────────────────
    for li, _ in loop_map:
        r = max(0.0, min(1.0, 1.0 + dr[li]))
        g = max(0.0, min(1.0,       dg[li]))
        b = max(0.0, min(1.0,       db[li]))
        setattr(color_data[li], color_attr, (r, g, b, 1.0))

    mesh.update()
    print(f"[tee_par3] Done — {n_loops} loops painted.")


paint_tee_par3()
