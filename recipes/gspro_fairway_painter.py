"""
Fairway Vertex Painter — standalone Blender Add-on
=====================================================
Installs a panel in View3D > Sidebar > Fairway Painter with one-click
vertex painting for GSPro golf course fairway meshes.

This is a self-contained extract from gspro_tee_painter.py containing
only the fairway recipe.  Install and use independently of the full
Course Vertex Recipes add-on.

Install:
  Edit > Preferences > Add-ons > Install… > select this file > Enable
"""

bl_info = {
    "name":        "Fairway Vertex Painter",
    "author":      "@chickenandribs",
    "version":     (1, 0, 0),
    "blender":     (3, 0, 0),
    "location":    "View3D > Sidebar > Fairway Painter",
    "description": "Vertex paint recipe for GSPro golf course fairway meshes",
    "category":    "Paint",
}

import bpy
import bmesh
import random
import math
from mathutils import Vector
from mathutils.kdtree import KDTree


# ══════════════════════════════════════════════════════════════════════════════
# SHARED PAINTING UTILITIES
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


def _mesh_diagonal(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(v.x for v in corners),
                 min(v.y for v in corners),
                 min(v.z for v in corners)))
    hi = Vector((max(v.x for v in corners),
                 max(v.y for v in corners),
                 max(v.z for v in corners)))
    return (hi - lo).length


def _build_loop_map(mesh, obj):
    """List of (loop_index, world_position, vertex_index) for every loop."""
    result = []
    mw = obj.matrix_world
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            result.append((li, mw @ mesh.vertices[vi].co, vi))
    return result


def _random_interior_point(mesh, obj, interior_mask):
    """Pick a random polygon centre whose vertices are all interior."""
    interior_polys = [p for p in mesh.polygons
                      if all(interior_mask[mesh.loops[li].vertex_index]
                             for li in p.loop_indices)]
    if not interior_polys:
        interior_polys = list(mesh.polygons)
    return obj.matrix_world @ random.choice(interior_polys).center


def _apply_deltas(loop_map, color_data, color_attr, dr, dg, db):
    for li, _, _vi in loop_map:
        r = max(0.0, min(1.0, 1.0 + dr[li]))
        g = max(0.0, min(1.0,       dg[li]))
        b = max(0.0, min(1.0,       db[li]))
        setattr(color_data[li], color_attr, (r, g, b, 1.0))


def _add_organic_blobs(loop_map, mesh, obj, dr, dg, db,
                       count, radius_frac, strength, channel,
                       diagonal, falloff, interior_mask):
    """Like _add_blobs but each blob is a randomly oriented ellipse for a
    more natural, less uniform appearance."""
    radius = diagonal * radius_frac
    for _ in range(count):
        centre     = _random_interior_point(mesh, obj, interior_mask)
        angle      = random.uniform(0, math.pi)
        axis       = Vector((math.cos(angle), math.sin(angle), 0.0))
        elongation = random.uniform(1.8, 4.0)
        jitter     = random.uniform(0.5, 1.0)

        for li, wco, vi in loop_map:
            if not interior_mask[vi]:
                continue
            diff       = wco - centre
            along      = diff.dot(axis)
            cross      = (diff - axis * along).length
            t = math.sqrt((along / (radius * elongation)) ** 2 +
                          (cross / radius) ** 2)
            if t >= 1.0:
                continue
            w = math.exp(-falloff * t * t) * strength * jitter
            if w == 0.0:
                continue
            if channel == "dark":
                dr[li] = min(dr[li], -w)
                dg[li] = min(dg[li], -w)
                db[li] = min(db[li], -w)
            elif channel == "blue":
                db[li] = max(db[li], w)
                dr[li] = min(dr[li], -w * 0.15)


def _height_gradient(loop_map, vert_noise, dr, dg, db,
                     dark_strength, blue_strength, blue_threshold=0.0):
    """Weight dark/blue influence by terrain elevation.
    Low vertices trend dark; high vertices trend blue (dry).
    blue_threshold (0–1): elevation fraction below which no blue is applied."""
    z_vals  = [wco.z for _, wco, _ in loop_map]
    z_min   = min(z_vals)
    z_range = max(max(z_vals) - z_min, 0.001)

    for li, wco, vi in loop_map:
        t = (wco.z - z_min) / z_range
        n = vert_noise[vi]

        if dark_strength > 0:
            dark_w = (1.0 - t) * dark_strength * (0.3 + 0.7 * n)
            dr[li] = min(dr[li], -dark_w)
            dg[li] = min(dg[li], -dark_w)
            db[li] = min(db[li], -dark_w)

        if blue_strength > 0 and t > blue_threshold:
            t_remapped = (t - blue_threshold) / (1.0 - blue_threshold)
            blue_w = t_remapped * blue_strength * (0.3 + 0.7 * n)
            db[li] = max(db[li], blue_w)
            dr[li] = min(dr[li], dr[li] - blue_w * 0.15)


def _hash2(cx, cy):
    """Deterministic pseudo-random value (0–1) for an integer grid coordinate."""
    h = (cx * 1664525 + cy * 1013904223) & 0x7FFFFFFF
    return h / 0x7FFFFFFF


def _smooth_noise_2d(pos, scale):
    """Smooth value noise: bilinearly interpolates hash at four surrounding grid
    corners using a smoothstep curve, preventing striping on aligned meshes."""
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


def _build_material_kd(scene, obj, mat_names):
    """Build a KDTree of world-space vertex positions from every mesh in the
    scene whose material slots contain any of the given material names."""
    positions = []
    for other in scene.objects:
        if other is obj or other.type != "MESH":
            continue
        mesh_mats = {slot.material.name for slot in other.material_slots if slot.material}
        if not (mat_names & mesh_mats):
            continue
        mw = other.matrix_world
        for vert in other.data.vertices:
            positions.append(mw @ vert.co)

    if not positions:
        return None

    kd = KDTree(len(positions))
    for i, pos in enumerate(positions):
        kd.insert(pos, i)
    kd.balance()
    return kd


def _apply_green_blend_protection(obj, loop_map, color_data, color_attr, radius):
    """Force-paint pure red on any vertex within `radius` metres of a
    'Green - Blend' mesh, ensuring a clean seam at the putting surface edge."""
    kd = _build_material_kd(bpy.context.scene, obj, {"Green - Blend"})
    if kd is None:
        return
    for li, wco, _ in loop_map:
        _, _, dist = kd.find(wco)
        if dist <= radius:
            setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))


def _read_color_layer(mesh):
    """Return (data, domain) for the best available colour layer on a mesh."""
    if hasattr(mesh, "color_attributes"):
        candidates = []
        if "Col" in mesh.color_attributes:
            candidates.append(mesh.color_attributes["Col"])
        if mesh.color_attributes.active_color:
            candidates.append(mesh.color_attributes.active_color)
        candidates += list(mesh.color_attributes)
        for attr in candidates:
            if attr is not None:
                return attr.data, attr.domain
    if mesh.vertex_colors:
        layer = mesh.vertex_colors.active or (mesh.vertex_colors[0] if mesh.vertex_colors else None)
        if layer:
            return layer.data, "CORNER"
    return None, None


def _sample_neighbor_blues(scene, obj, exclude_mats=None):
    """Build a KDTree of vertex positions/blues from every mesh geometrically
    attached to obj's open boundary (shares at least one colocated vertex)."""
    if exclude_mats is None:
        exclude_mats = {"Bunker"}
    mw   = obj.matrix_world
    mesh = obj.data

    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_vis = {v.index for e in bm.edges if e.is_boundary for v in e.verts}
    bm.free()

    if not boundary_vis:
        return None, []

    b_positions = [mw @ mesh.vertices[vi].co for vi in boundary_vis]
    b_kd = KDTree(len(b_positions))
    for i, pos in enumerate(b_positions):
        b_kd.insert(pos, i)
    b_kd.balance()

    bx = [p.x for p in b_positions]
    by = [p.y for p in b_positions]
    bbox_min_x, bbox_max_x = min(bx) - 1.0, max(bx) + 1.0
    bbox_min_y, bbox_max_y = min(by) - 1.0, max(by) + 1.0

    positions = []
    blues     = []

    for other in scene.objects:
        if other is obj or other.type != "MESH":
            continue
        other_mats = {s.material.name for s in other.material_slots if s.material}
        if exclude_mats & other_mats:
            continue

        oc = [other.matrix_world @ Vector(c) for c in other.bound_box]
        if (max(c.x for c in oc) < bbox_min_x or min(c.x for c in oc) > bbox_max_x or
                max(c.y for c in oc) < bbox_min_y or min(c.y for c in oc) > bbox_max_y):
            continue

        omesh = other.data
        omw   = other.matrix_world
        cd, dom = _read_color_layer(omesh)
        if cd is None:
            continue

        if not any(b_kd.find(omw @ v.co)[2] < 0.05 for v in omesh.vertices):
            continue

        vb = {}
        vc = {}
        for poly in omesh.polygons:
            for li in poly.loop_indices:
                vi  = omesh.loops[li].vertex_index
                idx = vi if dom == "POINT" else li
                try:
                    b = cd[idx].color[2]
                except Exception:
                    b = 0.0
                vb[vi] = vb.get(vi, 0.0) + b
                vc[vi] = vc.get(vi, 0)   + 1

        if not vb:
            continue

        for vi, total in vb.items():
            positions.append(omw @ omesh.vertices[vi].co)
            blues.append(total / vc[vi])

    if not positions:
        return None, []

    kd = KDTree(len(positions))
    for i, pos in enumerate(positions):
        kd.insert(pos, i)
    kd.balance()
    return kd, blues


def _apply_rough_seam_blend(obj, loop_map, color_data, color_attr, scene,
                            seam_radius=5.0):
    """Blend boundary vertices toward adjacent already-painted meshes so seams
    between neighbouring sections disappear."""
    mw   = obj.matrix_world
    mesh = obj.data

    kd, neighbor_blues = _sample_neighbor_blues(
        scene, obj, exclude_mats={"Bunker", "Tee - Blend", "Concrete - Blend"})
    if kd is None:
        return

    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_vis = {v.index for e in bm.edges if e.is_boundary for v in e.verts}
    bm.free()

    boundary_blue = {}
    for vi in boundary_vis:
        wco = mw @ mesh.vertices[vi].co
        _, idx, dist = kd.find(wco)
        if dist < 1.0:
            boundary_blue[vi] = neighbor_blues[idx]

    if not boundary_blue:
        return

    b_positions = [mw @ mesh.vertices[vi].co for vi in boundary_blue]
    b_blues     = list(boundary_blue.values())
    bkd = KDTree(len(b_positions))
    for i, pos in enumerate(b_positions):
        bkd.insert(pos, i)
    bkd.balance()

    for li, wco, vi in loop_map:
        if vi in boundary_blue:
            target = boundary_blue[vi]
            weight = 1.0
        else:
            _, bidx, bdist = bkd.find(wco)
            if bdist >= seam_radius:
                continue
            t = bdist / seam_radius
            weight = 1.0 - t * t * (3.0 - 2.0 * t)
            target = b_blues[bidx]

        cur = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        delta = (target - b) * weight
        setattr(color_data[li], color_attr,
                (max(0.0, min(1.0, r - delta)), g,
                 max(0.0, min(1.0, b + delta)), a))


def _boundary_one_side_distances(mesh, obj):
    """Walk the open boundary chain, take the first half (one long side), and
    return per-vertex perpendicular distance to the nearest segment on that half."""
    mw = obj.matrix_world

    bm = bmesh.new()
    bm.from_mesh(mesh)
    b_adj = {}
    for e in bm.edges:
        if e.is_boundary:
            i0, i1 = e.verts[0].index, e.verts[1].index
            b_adj.setdefault(i0, []).append(i1)
            b_adj.setdefault(i1, []).append(i0)
    bm.free()

    if not b_adj:
        return [0.0] * len(mesh.vertices)

    endpoints = [v for v, nbrs in b_adj.items() if len(nbrs) == 1]
    start     = endpoints[0] if endpoints else next(iter(b_adj))

    chain = []
    prev, curr = None, start
    while True:
        chain.append(curr)
        nbrs = [n for n in b_adj.get(curr, []) if n != prev]
        if not nbrs:
            break
        nxt = nbrs[0]
        if nxt == start and len(chain) > 2:
            break
        prev, curr = curr, nxt

    half_pts = [mw @ mesh.vertices[vi].co for vi in chain[: len(chain) // 2 + 1]]

    if len(half_pts) < 2:
        return [0.0] * len(mesh.vertices)

    dists = []
    for v in mesh.vertices:
        wco   = mw @ v.co
        min_d = 1e9
        for i in range(len(half_pts) - 1):
            p1, p2    = half_pts[i], half_pts[i + 1]
            seg       = p2 - p1
            seg_len_sq = seg.dot(seg)
            if seg_len_sq < 1e-6:
                d = (wco - p1).length
            else:
                t = max(0.0, min(1.0, (wco - p1).dot(seg) / seg_len_sq))
                d = (wco - (p1 + seg * t)).length
            if d < min_d:
                min_d = d
        dists.append(min_d)
    return dists


def _apply_fairway_mowstripes(obj, loop_map, color_data, color_attr,
                               stripe_period=3.0, noise_warp=0.3):
    """Post-process fairway with smooth R/G mow stripes that follow the fairway
    curve.  Blue channel (dry splotches) is preserved.  Dark apron verts are skipped."""
    mesh = obj.data

    vert_dist   = _boundary_one_side_distances(mesh, obj)
    NOISE_SCALE = 20.0

    for li, wco, vi in loop_map:
        cur = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]

        if r < 0.3 and g < 0.3:
            continue

        n     = _smooth_noise_2d(wco, NOISE_SCALE)
        coord = vert_dist[vi] + (n - 0.5) * noise_warp * stripe_period

        t = 0.5 + 0.5 * math.sin(coord / stripe_period * 2.0 * math.pi)
        setattr(color_data[li], color_attr, (1.0 - t, t, b, a))


def _apply_apron_black(obj, loop_map, color_data, color_attr, scene, radius=20.0):
    """Black out red vertices within radius metres of any mesh whose material
    is 'Green - Blend' (putting-surface transition meshes)."""
    kd = _build_material_kd(scene, obj, {"Green - Blend"})
    if kd is None:
        return

    blend_zone = min(5.0, radius * 0.25)
    inner      = radius - blend_zone

    for li, wco, _vi in loop_map:
        _, _, dist = kd.find(wco)
        if dist >= radius:
            continue
        cur = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        if dist <= inner:
            weight = 1.0
        else:
            t      = (dist - inner) / blend_zone
            weight = 1.0 - t * t * (3.0 - 2.0 * t)
        new_r = r * (1.0 - weight)
        new_g = g * (1.0 - weight)
        setattr(color_data[li], color_attr, (new_r, new_g, b, a))


# ══════════════════════════════════════════════════════════════════════════════
# FAIRWAY PAINTER
# Base color: Red (R=1, G=0, B=0)
# R channel = base fairway surface
# B channel = dry patches  (preferred on higher ground)
# Darkness  = reduce all channels  (preferred on lower ground)
# No green channel (no divots on fairways)
# Green-Blend adjacency: 2 m strip of pure red where fairway meets putting surface
# ══════════════════════════════════════════════════════════════════════════════

def paint_fairway(obj, seed, height_blue=0.03, patch_blue=0.03, apron_black=False,
                  mow_stripes=False, mow_stripe_period=3.0,
                  use_height_blue=True, use_patch_blue=True,
                  use_seam_blend=True, use_green_protect=True,
                  apron_radius=20.0):
    mesh = obj.data
    if seed >= 0:
        random.seed(seed)

    color_data, color_attr = _ensure_color_layer(mesh)
    diag     = _mesh_diagonal(obj)
    loop_map = _build_loop_map(mesh, obj)
    n        = len(mesh.loops)

    vert_noise = [random.random() for _ in range(len(mesh.vertices))]
    interior   = [True] * len(mesh.vertices)

    # Flood-fill red
    for li, _, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    dr = [0.0] * n
    dg = [0.0] * n
    db = [0.0] * n

    if use_height_blue:
        _height_gradient(loop_map, vert_noise, dr, dg, db,
                         dark_strength=0.0, blue_strength=height_blue, blue_threshold=0.5)

    if use_patch_blue:
        _add_organic_blobs(loop_map, mesh, obj, dr, dg, db,
                           count=2, radius_frac=0.09, strength=patch_blue,
                           channel="blue", diagonal=diag, falloff=2.5, interior_mask=interior)

    _apply_deltas(loop_map, color_data, color_attr, dr, dg, db)

    if use_seam_blend:
        _apply_rough_seam_blend(obj, loop_map, color_data, color_attr, bpy.context.scene,
                                seam_radius=4.0)

    if use_green_protect:
        _apply_green_blend_protection(obj, loop_map, color_data, color_attr, radius=2.0)

    if apron_black:
        _apply_apron_black(obj, loop_map, color_data, color_attr, bpy.context.scene,
                           radius=apron_radius)

    # Mow stripes last so they override R/G on all non-dark vertices
    if mow_stripes:
        _apply_fairway_mowstripes(obj, loop_map, color_data, color_attr,
                                   stripe_period=mow_stripe_period)

    mesh.update()


# ══════════════════════════════════════════════════════════════════════════════
# OPERATOR
# ══════════════════════════════════════════════════════════════════════════════

class CVRF_OT_paint_fairway(bpy.types.Operator):
    bl_idname      = "cvrf.paint_fairway"
    bl_label       = "Fairway"
    bl_description = ("Vertex paint selected meshes as a fairway — height-driven "
                      "dry variation, organic patches, clean seam at Green-Blend edges")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvrf_props
        seed    = props.seed if props.use_seed else -1
        painted = 0
        for obj in context.selected_objects:
            if obj.type == "MESH":
                paint_fairway(obj, seed,
                              height_blue=props.fairway_height_blue,
                              patch_blue=props.fairway_patch_blue,
                              apron_black=props.fairway_apron_black,
                              mow_stripes=props.fairway_mow_stripes,
                              mow_stripe_period=props.fairway_stripe_period,
                              use_height_blue=props.fairway_use_height_blue,
                              use_patch_blue=props.fairway_use_patch_blue,
                              use_seam_blend=props.fairway_use_seam_blend,
                              use_green_protect=props.fairway_use_green_protect,
                              apron_radius=props.fairway_apron_radius)
                painted += 1
        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Fairway paint applied to {painted} object(s).")
        return {"FINISHED"}


# ══════════════════════════════════════════════════════════════════════════════
# PANEL
# ══════════════════════════════════════════════════════════════════════════════

class CVRF_PT_fairway(bpy.types.Panel):
    bl_label       = "Fairway Painter"
    bl_idname      = "CVRF_PT_fairway"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Fairway Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.cvrf_props

        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvrf.paint_fairway", icon="NODE_MATERIAL")

        layout.separator()

        # ── Height Gradient ──────────────────────────────────────────────────
        box = layout.box()
        box.prop(props, "fairway_use_height_blue",
                 text="Height Gradient  (Dry High Ground)")
        sub = box.column()
        sub.enabled = props.fairway_use_height_blue
        sub.prop(props, "fairway_height_blue", text="Strength")

        layout.separator()

        # ── Organic Dry Patches ──────────────────────────────────────────────
        box = layout.box()
        box.prop(props, "fairway_use_patch_blue",
                 text="Organic Dry Patches")
        sub = box.column()
        sub.enabled = props.fairway_use_patch_blue
        sub.prop(props, "fairway_patch_blue", text="Strength")

        layout.separator()

        # ── Seam Blend ───────────────────────────────────────────────────────
        box = layout.box()
        box.prop(props, "fairway_use_seam_blend",
                 text="Seam Blend  (Recommended)")

        layout.separator()

        # ── Green-Blend Protection ───────────────────────────────────────────
        box = layout.box()
        box.prop(props, "fairway_use_green_protect",
                 text="Green-Blend Protection")

        layout.separator()

        # ── Apron Black ──────────────────────────────────────────────────────
        box = layout.box()
        box.prop(props, "fairway_apron_black",
                 text="Paint Black on Apron")
        sub = box.column()
        sub.enabled = props.fairway_apron_black
        sub.prop(props, "fairway_apron_radius", text="Radius (m)")

        layout.separator()

        # ── Mow Stripes (pre-beta) ───────────────────────────────────────────
        box = layout.box()
        box.label(text="Mow Stripes  (prebeta — do not use)", icon="MOD_ARRAY")
        box.prop(props, "fairway_mow_stripes", text="Enable Mow Stripes (R/G)")
        sub = box.row()
        sub.enabled = props.fairway_mow_stripes
        sub.prop(props, "fairway_stripe_period", text="Stripe Width (m)")

        layout.separator()

        # ── Options ──────────────────────────────────────────────────────────
        box = layout.box()
        box.label(text="Options", icon="SETTINGS")
        row = box.row()
        row.prop(props, "use_seed", text="Fixed Seed")
        sub = row.row()
        sub.enabled = props.use_seed
        sub.prop(props, "seed", text="")


# ══════════════════════════════════════════════════════════════════════════════
# PROPERTIES
# ══════════════════════════════════════════════════════════════════════════════

class CVRFairwayProps(bpy.types.PropertyGroup):
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
    fairway_use_height_blue: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Height Gradient (Dry High Ground)",
        description = ("Adds a subtle blue (dry) tint to vertices on higher terrain. "
                       "Low areas stay pure red; ridges and rises get a bleached look. "
                       "Strength is set by the slider below."),
        default     = True,
    )
    fairway_height_blue: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Height Gradient",
        description = "How strongly elevation drives the dry (blue) tint on fairways",
        default     = 0.03,
        min         = 0.0,
        max         = 0.20,
        step        = 1,
        precision   = 3,
    )
    fairway_use_patch_blue: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Organic Dry Patches",
        description = ("Scatters 2 organic elliptical dry patches randomly across the "
                       "fairway surface. Each is randomly oriented and stretched for a "
                       "natural look. Strength is set by the slider below."),
        default     = True,
    )
    fairway_patch_blue: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Dry Patches",
        description = "Strength of the organic blob dry patches on fairways",
        default     = 0.03,
        min         = 0.0,
        max         = 0.20,
        step        = 1,
        precision   = 3,
    )
    fairway_use_seam_blend: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Seam Blend (Recommended)",
        description = ("Matches the blue channel at mesh boundaries to adjacent "
                       "already-painted meshes (rough, other fairways). Prevents hard "
                       "color edges where two painted sections meet."),
        default     = True,
    )
    fairway_use_green_protect: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Green-Blend Protection",
        description = ("Forces a 2 m strip of pure red (R=1, G=0, B=0) where the "
                       "fairway meets any Green-Blend transition mesh. Keeps the "
                       "putting-surface seam clean and sharp."),
        default     = True,
    )
    fairway_apron_black: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Paint Black on Apron (For Crossed Mowlines)",
        description = ("Darkens vertices to black within the apron radius of any "
                       "'Green - Blend' object. Creates a crossed mow-line look on the "
                       "apron zone around the putting surface. Radius set below."),
        default     = False,
    )
    fairway_apron_radius: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Apron Radius",
        description = ("Radius in metres around Green-Blend meshes that is darkened to "
                       "black when Paint Black on Apron is enabled. Default is 20 m."),
        default     = 20.0,
        min         = 5.0,
        max         = 50.0,
        step        = 100,
        precision   = 1,
    )
    fairway_mow_stripes: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Mow Stripes",
        description = "Paint alternating R/G stripes that follow the fairway boundary profile",
        default     = False,
    )
    fairway_stripe_period: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Stripe Width",
        description = "Width of each individual mow stripe in metres (1 R stripe + 1 G stripe = 2 × this value)",
        default     = 3.0,
        min         = 1.0,
        max         = 5.0,
        step        = 10,
        precision   = 2,
    )


# ══════════════════════════════════════════════════════════════════════════════
# REGISTRATION
# ══════════════════════════════════════════════════════════════════════════════

_classes = (
    CVRFairwayProps,
    CVRF_OT_paint_fairway,
    CVRF_PT_fairway,
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.cvrf_props = bpy.props.PointerProperty(type=CVRFairwayProps)


def unregister():
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
    del bpy.types.Scene.cvrf_props


if __name__ == "__main__":
    register()
