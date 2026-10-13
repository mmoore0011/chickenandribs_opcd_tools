"""
Tee Box Vertex Paint Recipe - Par 4 / Par 5
=============================================
Paints a tee box mesh with realistic wear for a par 4 or 5:
  Red   = base tee surface (dominant)
  Black = dark worn / scuffed spots
  Blue  = dry / sun-bleached patches

No green (divots) — par 4/5 tees see iron/wood shots, not repeated
short-iron divots like a par 3.

Run from Blender's Scripting editor with the tee mesh selected.
Works with Blender 3.x and 4.x.
"""

import bpy
import random
import math
from mathutils import Vector

# ── Tuning ────────────────────────────────────────────────────────────────────
SEED = None          # int for reproducible runs, None for random each time

# Blob counts
N_DARK_PATCHES = 5
N_DRY_PATCHES  = 10   # par 4/5 tees tend to be drier / more sun-worn

# Blob size as fraction of mesh diagonal
DARK_RADIUS_FRAC = 0.13   # slightly larger, softer dark zones
DRY_RADIUS_FRAC  = 0.15   # broader dry patches

# Peak strength
DARK_STRENGTH = 0.18
DRY_STRENGTH  = 0.20   # a bit more pronounced dryness on longer tees

FALLOFF = 3.0          # slightly softer than par 3 recipe
# ──────────────────────────────────────────────────────────────────────────────


def _ensure_color_layer(mesh, name="Col"):
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
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
    hi = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
    return (hi - lo).length


def _build_loop_vertex_map(mesh):
    loop_pos = []
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            loop_pos.append((li, mesh.vertices[vi].co.copy()))
    return loop_pos


def _gaussian(dist, radius):
    if dist >= radius:
        return 0.0
    t = dist / radius
    return math.exp(-FALLOFF * t * t)


def _random_surface_point(mesh, obj):
    poly = random.choice(mesh.polygons)
    return obj.matrix_world @ poly.center


def paint_tee_par45():
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

    n_loops = len(mesh.loops)
    dr = [0.0] * n_loops
    db = [0.0] * n_loops

    def add_blobs(count, radius_frac, strength, channel):
        radius = diagonal * radius_frac
        for _ in range(count):
            centre = _random_surface_point(mesh, obj)
            for li, local_co in loop_map:
                world_co = obj.matrix_world @ local_co
                dist = (world_co - centre).length
                w = _gaussian(dist, radius) * strength * random.uniform(0.4, 1.0)
                if channel == "dark":
                    dr[li] = min(dr[li], -w)
                elif channel == "blue":
                    db[li] = max(db[li], w)
                    dr[li] = min(dr[li], -w * 0.25)

    add_blobs(N_DARK_PATCHES, DARK_RADIUS_FRAC, DARK_STRENGTH, "dark")
    add_blobs(N_DRY_PATCHES,  DRY_RADIUS_FRAC,  DRY_STRENGTH,  "blue")

    # ── Step 2: apply accumulated deltas ─────────────────────────────────────
    for li, _ in loop_map:
        r = max(0.0, min(1.0, 1.0 + dr[li]))
        g = 0.0
        b = max(0.0, min(1.0,       db[li]))
        setattr(color_data[li], color_attr, (r, g, b, 1.0))

    mesh.update()
    print(f"[tee_par45] Done — {n_loops} loops painted.")


paint_tee_par45()
