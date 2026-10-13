"""
FTR Painter — Blender Add-on
========================================
Installs a panel in View3D > Sidebar > FTR Painter for Fore The Realm
course-specific tooling: mesh optimization plus FTR's own vertex-paint
recipes, in the spirit of the Course Vertex Recipes add-on
(gspro_tee_painter.py / gspro_fairway_painter.py) but built around how this
course is actually put together — no blend meshes between surfaces, and a
very large scene (multiple holes per chapter, far enough apart for separate
SkyDomes), so recipes here use geometric-only seam/boundary detection (no
material-name-based lookups) and a KDTree rather than all-pairs scans, to
scale to this course's size.

Sections:
  - Mesh Optimization    (strip a mesh's interior to a light triangle fill,
                          keeping its exact boundary loop so neighboring
                          terrain-conforming meshes stay connected)
  - Snow Scene
      - Fairway              (red base, blue snow line in from the boundary
                              at a randomly varying depth, capped at a hard
                              maximum)
      - Rough (Slope-Based)  (blue on flat ground, red on steep faces,
                              blended smoothly by each vertex's surface angle)
  - More recipes to come

Install:
  Edit > Preferences > Add-ons > Install… > select this file > Enable
"""

bl_info = {
    "name":        "FTR Painter",
    "author":      "@chickenandribs",
    "version":     (1, 0, 0),
    "blender":     (3, 0, 0),
    "location":    "View3D > Sidebar > FTR Painter",
    "description": "Fore The Realm course tooling: mesh optimization + FTR vertex paint recipes",
    "category":    "Paint",
}

import bpy
import bmesh
import random
import math
from mathutils import Vector
from mathutils.kdtree import KDTree


# ══════════════════════════════════════════════════════════════════════════════
# MESH OPTIMIZATION
# Many terrain tiles (e.g. distant "Deep" rough, tens of thousands of
# triangles each) are never actually seen up close in game — replacing them
# with a much lighter mesh, in the same material, cuts their triangle count
# dramatically with no visible difference from normal play distance.
#
# The interior gets discarded and re-filled with a light triangulation, but
# the outer BOUNDARY LOOP is kept exactly as it was (same vertex count, same
# positions) — a plain bounding-box quad was tried first, but
# mesh_gap_detector.py's snap tools only move existing vertices to their
# nearest match, they don't add new ones. A 4-corner quad snapped against a
# dense terrain-conforming neighbor only connects at those 4 points, leaving
# everything between them open. Keeping the real boundary loop means it's
# still exactly as connected to its neighbors as it was before optimizing —
# ideally closing the seam outright, with the gap tools as a fine-tuning
# backstop rather than doing all the work.
# ══════════════════════════════════════════════════════════════════════════════

def _triangle_count(mesh):
    """Estimate triangle count without requiring calc_loop_triangles() — an
    N-gon contributes N-2 triangles when triangulated."""
    return sum(max(len(p.vertices) - 2, 0) for p in mesh.polygons)


def _replace_with_bbox_plane(obj, materials, old_name):
    """Fallback for meshes with no open boundary (e.g. a closed/watertight
    solid) — nothing to preserve, so just span the bounding footprint."""
    corners = [Vector(c) for c in obj.bound_box]  # local-space
    xs = [c.x for c in corners]
    ys = [c.y for c in corners]
    zs = [c.z for c in corners]
    min_x, max_x = min(xs), max(xs)
    min_y, max_y = min(ys), max(ys)
    z = (min(zs) + max(zs)) / 2.0

    new_mesh = bpy.data.meshes.new(old_name + "_ftrp_tmp")
    verts = [
        (min_x, min_y, z), (max_x, min_y, z),
        (max_x, max_y, z), (min_x, max_y, z),
    ]
    new_mesh.from_pydata(verts, [], [(0, 1, 2, 3)])
    new_mesh.update()
    for mat in materials:
        new_mesh.materials.append(mat)
    return new_mesh


def _replace_with_plane(obj):
    """Swap obj's mesh data for a much lighter version that keeps the exact
    same open-boundary loop (vertex count and positions unchanged) while
    discarding and re-filling the interior with a light triangulation. The
    object itself (name, transform, parenting) is untouched — only its mesh
    data changes."""
    old_mesh = obj.data
    old_name = old_mesh.name
    materials = list(old_mesh.materials)

    bm = bmesh.new()
    bm.from_mesh(old_mesh)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()

    boundary_edges = [e for e in bm.edges if e.is_boundary]

    if not boundary_edges:
        bm.free()
        new_mesh = _replace_with_bbox_plane(obj, materials, old_name)
    else:
        boundary_verts = {v for e in boundary_edges for v in e.verts}

        # Strip faces, then every vertex that isn't on the boundary — this
        # cascades to remove their edges too, leaving just the boundary loop(s).
        bmesh.ops.delete(bm, geom=list(bm.faces), context="FACES_ONLY")
        interior_verts = [v for v in bm.verts if v not in boundary_verts]
        bmesh.ops.delete(bm, geom=interior_verts, context="VERTS")

        # What's left should be just the boundary loop(s) — fill with a light,
        # "beauty" triangulation (the same operation behind Blender's own
        # Grid/Fill mesh commands).
        bmesh.ops.triangle_fill(bm, use_beauty=True, edges=list(bm.edges))
        bm.normal_update()

        new_mesh = bpy.data.meshes.new(old_name + "_ftrp_tmp")
        bm.to_mesh(new_mesh)
        bm.free()
        new_mesh.update()
        for mat in materials:
            new_mesh.materials.append(mat)

    obj.data = new_mesh
    bpy.data.meshes.remove(old_mesh)
    new_mesh.name = old_name


class FTRP_OT_optimize_to_plane(bpy.types.Operator):
    bl_idname      = "ftrp.optimize_to_plane"
    bl_label       = "Replace Selected with Plane"
    bl_description = ("Replace each selected mesh's interior with a light triangle fill, "
                      "keeping its exact original boundary loop (same vertex count and "
                      "positions) so neighboring terrain-conforming meshes stay connected — "
                      "for meshes far enough from play that their real geometry is never "
                      "actually seen (e.g. distant deep-rough tiles)")
    bl_options     = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT" and any(o.type == "MESH" for o in context.selected_objects)

    def execute(self, context):
        count = 0
        tris_before = 0
        tris_after = 0
        for obj in list(context.selected_objects):
            if obj.type != "MESH":
                continue
            tris_before += _triangle_count(obj.data)
            _replace_with_plane(obj)
            tris_after += _triangle_count(obj.data)
            count += 1

        if count == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}

        self.report({"INFO"},
                    f"Replaced {count} mesh(es) "
                    f"(was ~{tris_before:,} triangles, now ~{tris_after:,}).")
        return {"FINISHED"}


# ══════════════════════════════════════════════════════════════════════════════
# SHARED VERTEX-PAINT UTILITIES
# Same implementations as gspro_fairway_painter.py/gspro_tee_painter.py — kept
# duplicated here rather than imported, so this add-on stays a single
# self-contained file, same convention those two already follow.
# ══════════════════════════════════════════════════════════════════════════════

def _ensure_color_layer(mesh, name="Col"):
    """Return (data_array, attribute_name) for the active vertex-color layer."""
    if hasattr(mesh, "color_attributes"):          # Blender 4.x
        if name not in mesh.color_attributes:
            mesh.color_attributes.new(name, "FLOAT_COLOR", "CORNER")
        attr = mesh.color_attributes[name]
        mesh.color_attributes.active_color = attr
        return attr.data, "color"
    else:                                           # Blender 3.x
        if not mesh.vertex_colors:
            mesh.vertex_colors.new(name=name)
        layer = (mesh.vertex_colors[name]
                 if name in mesh.vertex_colors
                 else mesh.vertex_colors.active)
        return layer.data, "color"


def _build_loop_map(mesh, obj):
    """List of (loop_index, world_position, vertex_index) for every loop."""
    result = []
    mw = obj.matrix_world
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            result.append((li, mw @ mesh.vertices[vi].co, vi))
    return result


# ══════════════════════════════════════════════════════════════════════════════
# SNOW SCENE
# Two recipes for a snowed-in look: Snow-Lined Fairway (blue in from the
# boundary) and Rough (Slope-Based) (blue on flat ground, red on steep faces,
# where snow wouldn't stick).
# ══════════════════════════════════════════════════════════════════════════════

# ── Snow-Lined Fairway ──────────────────────────────────────────────────────
# Base color: Red (R=1, G=0, B=0), matching Course Vertex Recipes' fairway
# convention. Blue is painted in from the open boundary toward the center,
# penetration depth varying smoothly (via 2D noise) along the edge's length
# so it reads as drifted/melting snow rather than a uniform band, capped at
# a hard maximum so it never reaches past a few meters in.

def _distance_to_boundary(mesh, obj):
    """Per-vertex distance (world units) to the nearest open-boundary vertex,
    via a KDTree rather than an all-pairs scan — this course's meshes can be
    large, so this needs to scale past a handful of boundary points."""
    mw = obj.matrix_world

    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_vis = list({v.index for e in bm.edges if e.is_boundary for v in e.verts})
    bm.free()

    n = len(mesh.vertices)
    if not boundary_vis:
        return [float("inf")] * n

    kd = KDTree(len(boundary_vis))
    for i, vi in enumerate(boundary_vis):
        kd.insert(mw @ mesh.vertices[vi].co, i)
    kd.balance()

    dists = [0.0] * n
    for vi, vert in enumerate(mesh.vertices):
        _, _, d = kd.find(mw @ vert.co)
        dists[vi] = d
    return dists


def _hash2(cx, cy):
    """Deterministic pseudo-random value (0–1) for an integer grid coordinate."""
    h = (cx * 1664525 + cy * 1013904223) & 0x7FFFFFFF
    return h / 0x7FFFFFFF


def _smooth_noise_2d(pos, scale):
    """Smooth value noise: bilinearly interpolates hash at four surrounding grid
    corners using a smoothstep curve, avoiding a jagged/striped snow line."""
    sx = pos.x / scale
    sy = pos.y / scale
    ix = int(math.floor(sx))
    iy = int(math.floor(sy))
    fx = sx - ix
    fy = sy - iy
    ux = fx * fx * (3.0 - 2.0 * fx)
    uy = fy * fy * (3.0 - 2.0 * fy)
    v00 = _hash2(ix,     iy)
    v10 = _hash2(ix + 1, iy)
    v01 = _hash2(ix,     iy + 1)
    v11 = _hash2(ix + 1, iy + 1)
    return (v00 * (1.0 - ux) * (1.0 - uy) +
            v10 * ux         * (1.0 - uy) +
            v01 * (1.0 - ux) * uy         +
            v11 * ux         * uy)


def paint_snow_fairway(obj, seed, max_depth=3.0, noise_scale=6.0, blend_width=0.5):
    mesh = obj.data
    if seed >= 0:
        random.seed(seed)

    color_data, color_attr = _ensure_color_layer(mesh)
    loop_map = _build_loop_map(mesh, obj)

    # Flood red base first, per the same convention as Course Vertex Recipes' fairway.
    for li, _, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    vert_dist = _distance_to_boundary(mesh, obj)

    # Randomizes where the noise field lands per run/object, so identical
    # meshes don't all get an identical-looking snow line.
    noise_offset = Vector((random.uniform(0.0, 10000.0), random.uniform(0.0, 10000.0), 0.0))

    for li, wco, vi in loop_map:
        d = vert_dist[vi]
        if d >= max_depth:
            continue  # too far from any edge to ever be snow — stays pure red

        # This vertex's local section of edge gets its own random depth (0..max_depth).
        n = _smooth_noise_2d(wco + noise_offset, noise_scale)
        threshold = max_depth * n
        if d >= threshold:
            continue  # past this section's random depth — stays red

        # Smooth blend back to red over the final blend_width meters, rather
        # than a hard cutoff at the threshold.
        remaining = threshold - d
        weight = 1.0 if blend_width <= 0.0 else min(1.0, remaining / blend_width)
        weight = weight * weight * (3.0 - 2.0 * weight)  # smoothstep

        cur = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        new_r = r * (1.0 - weight)
        new_b = b + (1.0 - b) * weight
        setattr(color_data[li], color_attr, (new_r, g, new_b, a))

    mesh.update()


class FTRP_OT_paint_snow_fairway(bpy.types.Operator):
    bl_idname      = "ftrp.paint_snow_fairway"
    bl_label       = "Snow-Lined Fairway"
    bl_description = ("Flood paint red, then paint blue in from the fairway's open "
                      "boundary toward the center at a randomly varying depth (never "
                      "exceeding Max Depth), for a natural drifted/melting snow-line look")
    bl_options     = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT" and any(o.type == "MESH" for o in context.selected_objects)

    def execute(self, context):
        props = context.scene.ftrp_props
        seed = props.seed if props.use_seed else -1
        painted = 0
        for obj in context.selected_objects:
            if obj.type == "MESH":
                paint_snow_fairway(obj, seed,
                                   max_depth=props.snow_max_depth,
                                   noise_scale=props.snow_noise_scale,
                                   blend_width=props.snow_blend_width)
                painted += 1

        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Snow-lined fairway paint applied to {painted} object(s).")
        return {"FINISHED"}


# ── Rough (Slope-Based) ──────────────────────────────────────────────────────
# Blue where the surface is flat (snow settles), red where it's steep (snow
# doesn't stick), with a smooth, continuously-varying ratio in between —
# driven purely by each vertex's own (smoothed) normal angle against world-up,
# not by height or position, so it reacts correctly to slopes facing any
# direction.

def paint_rough_slope(obj, curve=1.0):
    mesh = obj.data
    color_data, color_attr = _ensure_color_layer(mesh)
    loop_map = _build_loop_map(mesh, obj)

    normal_mat = obj.matrix_world.to_3x3().inverted().transposed()
    up = Vector((0.0, 0.0, 1.0))
    half_pi = math.pi / 2.0

    vert_t = {}  # cache per-vertex ratio — shared by every loop touching that vertex
    for li, _wco, vi in loop_map:
        t = vert_t.get(vi)
        if t is None:
            world_n = (normal_mat @ mesh.vertices[vi].normal).normalized()
            dot = max(-1.0, min(1.0, world_n.dot(up)))
            angle = math.acos(dot)                # 0 = flat (up-facing) .. pi/2 = vertical
            t = min(angle / half_pi, 1.0)          # clamp past-vertical (overhangs) to fully red
            if curve != 1.0:
                t = t ** curve
            vert_t[vi] = t

        setattr(color_data[li], color_attr, (t, 0.0, 1.0 - t, 1.0))

    mesh.update()


class FTRP_OT_paint_rough_slope(bpy.types.Operator):
    bl_idname      = "ftrp.paint_rough_slope"
    bl_label       = "Rough (Slope-Based)"
    bl_description = ("Paint blue on flat ground and red on steep faces, blended smoothly "
                      "in between by each vertex's own surface angle — flat holds snow, "
                      "steep faces show bare ground")
    bl_options     = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT" and any(o.type == "MESH" for o in context.selected_objects)

    def execute(self, context):
        props = context.scene.ftrp_props
        painted = 0
        for obj in context.selected_objects:
            if obj.type == "MESH":
                paint_rough_slope(obj, curve=props.rough_slope_curve)
                painted += 1

        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Slope-based rough paint applied to {painted} object(s).")
        return {"FINISHED"}


# ══════════════════════════════════════════════════════════════════════════════
# PROPERTIES
# ══════════════════════════════════════════════════════════════════════════════

class FTRPainterProps(bpy.types.PropertyGroup):
    use_seed: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Fixed Seed",
        description = "Use a fixed random seed for reproducible results",
        default     = False,
    )
    seed: bpy.props.IntProperty(  # type: ignore[assignment]
        name        = "Seed",
        description = "Random seed (only used when Fixed Seed is enabled)",
        default     = 42,
        min         = 0,
        max         = 99999,
    )
    snow_max_depth: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Max Depth",
        description = "Furthest the snow line can reach in from the edge, in meters",
        default     = 3.0,
        min         = 0.1,
        max         = 20.0,
        step        = 10,
        precision   = 2,
    )
    snow_noise_scale: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Variation Scale",
        description = "Wavelength (meters) of the random depth variation along the edge — smaller is more jagged/frequent",
        default     = 6.0,
        min         = 0.5,
        max         = 30.0,
        step        = 10,
        precision   = 1,
    )
    snow_blend_width: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Edge Softness",
        description = "How many meters the transition back to red spans, at the inner edge of the snow line",
        default     = 0.5,
        min         = 0.0,
        max         = 2.0,
        step        = 10,
        precision   = 2,
    )
    rough_slope_curve: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Slope Curve",
        description = ("Bias the flat/steep transition — 1.0 is a straight linear ratio by "
                       "angle; higher values keep more surface fully blue before red starts "
                       "creeping in, lower values do the opposite"),
        default     = 1.0,
        min         = 0.2,
        max         = 5.0,
        step        = 10,
        precision   = 2,
    )


# ══════════════════════════════════════════════════════════════════════════════
# PANEL
# ══════════════════════════════════════════════════════════════════════════════

class FTRP_PT_main(bpy.types.Panel):
    bl_label       = "FTR Painter"
    bl_idname      = "FTRP_PT_main"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "FTR Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.ftrp_props

        box = layout.box()
        box.label(text="Mesh Optimization", icon="MOD_DECIM")
        box.label(text="Select mesh(es) never seen up close")
        box.label(text="(e.g. distant deep-rough tiles), then:")
        col = box.column(align=True)
        col.scale_y = 1.4
        col.operator("ftrp.optimize_to_plane", icon="MESH_PLANE")

        layout.separator()

        outer = layout.box()
        outer.label(text="Snow Scene", icon="FREEZE")

        box = outer.box()
        box.label(text="Fairway", icon="VPAINT_HLT")
        col = box.column(align=True)
        col.scale_y = 1.4
        col.operator("ftrp.paint_snow_fairway", icon="VPAINT_HLT")
        box.prop(props, "snow_max_depth")
        box.prop(props, "snow_noise_scale")
        box.prop(props, "snow_blend_width")

        box = outer.box()
        box.label(text="Rough (Slope-Based)", icon="MOD_NOISE")
        col = box.column(align=True)
        col.scale_y = 1.4
        col.operator("ftrp.paint_rough_slope", icon="VPAINT_HLT")
        box.prop(props, "rough_slope_curve")

        layout.separator()

        box = layout.box()
        box.label(text="Options", icon="SETTINGS")
        row = box.row()
        row.prop(props, "use_seed", text="Fixed Seed")
        sub = row.row()
        sub.enabled = props.use_seed
        sub.prop(props, "seed", text="")


# ══════════════════════════════════════════════════════════════════════════════
# REGISTRATION
# ══════════════════════════════════════════════════════════════════════════════

_classes = (
    FTRP_OT_optimize_to_plane,
    FTRPainterProps,
    FTRP_OT_paint_snow_fairway,
    FTRP_OT_paint_rough_slope,
    FTRP_PT_main,
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.ftrp_props = bpy.props.PointerProperty(type=FTRPainterProps)


def unregister():
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
    del bpy.types.Scene.ftrp_props


if __name__ == "__main__":
    register()
