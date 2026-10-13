"""
Course Vertex Recipes — Blender Add-on
========================================
Installs a panel in View3D > Sidebar > Course Vertex Painter with one-click
vertex painting recipes for GSPro golf course meshes.

Sections:
  - Tee Boxes  (Par 3 / Par 4-5)
  - Fairways   (coming soon)
  - Rough      (coming soon)
  - Greens     (coming soon)

Install:
  Edit > Preferences > Add-ons > Install… > select this file > Enable
"""

bl_info = {
    "name":        "Course Vertex Recipes",
    "author":      "@chickenandribs",
    "version":     (1, 0, 0),
    "blender":     (3, 0, 0),
    "location":    "View3D > Sidebar > Course Vertex Painter",
    "description": "Vertex paint recipes for GSPro golf course meshes",
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
# Used by all surface-type recipes below.
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


def _build_interior_mask(mesh, obj, margin_frac):
    """Return a per-vertex bool list: True = far enough from boundary to paint.
    Margin is based on the shortest bounding-box side so narrow meshes
    don't get fully excluded."""
    mw = obj.matrix_world

    corners = [mw @ Vector(c) for c in obj.bound_box]
    extents = [
        max(v.x for v in corners) - min(v.x for v in corners),
        max(v.y for v in corners) - min(v.y for v in corners),
    ]
    threshold = max(min(extents), 0.001) * margin_frac

    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_verts = {v.index for e in bm.edges if e.is_boundary for v in e.verts}
    bm.free()

    if not boundary_verts:
        return [True] * len(mesh.vertices)

    boundary_positions = [mw @ mesh.vertices[vi].co for vi in boundary_verts]

    mask = []
    for vert in mesh.vertices:
        wco = mw @ vert.co
        min_dist = min((wco - bp).length for bp in boundary_positions)
        mask.append(min_dist >= threshold)

    if not any(mask):           # safety: margin too tight, paint everything
        return [True] * len(mesh.vertices)
    return mask


def _random_interior_point(mesh, obj, interior_mask):
    """Pick a random polygon centre whose vertices are all interior."""
    interior_polys = [p for p in mesh.polygons
                      if all(interior_mask[mesh.loops[li].vertex_index]
                             for li in p.loop_indices)]
    if not interior_polys:
        interior_polys = list(mesh.polygons)
    return obj.matrix_world @ random.choice(interior_polys).center


def _gaussian(dist, radius, falloff):
    if dist >= radius:
        return 0.0
    t = dist / radius
    return math.exp(-falloff * t * t)


def _add_blobs(loop_map, mesh, obj, dr, dg, db,
               count, radius_frac, strength, channel,
               diagonal, falloff, interior_mask):
    """Scatter Gaussian blobs of a given channel across interior vertices."""
    radius = diagonal * radius_frac
    for _ in range(count):
        centre = _random_interior_point(mesh, obj, interior_mask)
        jitter = random.uniform(0.5, 1.0)
        for li, wco, vi in loop_map:
            if not interior_mask[vi]:
                continue
            w = _gaussian((wco - centre).length, radius, falloff) * strength * jitter
            if w == 0.0:
                continue
            if channel == "dark":
                dr[li] = min(dr[li], -w)
                dg[li] = min(dg[li], -w)
                db[li] = min(db[li], -w)
            elif channel == "blue":
                db[li] = max(db[li], w)
                dr[li] = min(dr[li], -w * 0.15)
            elif channel == "green":
                dg[li] = max(dg[li], w)
                dr[li] = min(dr[li], -w * 0.5)


def _add_spaced_blobs(loop_map, mesh, obj, dr, dg, db,
                      min_separation, radius_frac, strength, channel,
                      diagonal, falloff, interior_mask):
    """Like _add_blobs but enforces a minimum world-space distance between
    blob centres.  Stops naturally when no more blobs fit."""
    radius = diagonal * radius_frac
    placed = []

    for _ in range(30):            # hard upper cap
        for _ in range(80):        # attempts per blob
            candidate = _random_interior_point(mesh, obj, interior_mask)
            if all((candidate - c).length >= min_separation for c in placed):
                break
        else:
            break                  # no room left — done

        placed.append(candidate)
        jitter = random.uniform(0.3, 1.0)
        for li, wco, vi in loop_map:
            if not interior_mask[vi]:
                continue
            w = _gaussian((wco - candidate).length, radius, falloff) * strength * jitter
            if w == 0.0:
                continue
            if channel == "dark":
                dr[li] = min(dr[li], -w)
                dg[li] = min(dg[li], -w)
                db[li] = min(db[li], -w)
            elif channel == "blue":
                db[li] = max(db[li], w)
                dr[li] = min(dr[li], -w * 0.15)
            elif channel == "green":
                dg[li] = max(dg[li], w)
                dr[li] = min(dr[li], -w * 0.5)


def _apply_deltas(loop_map, color_data, color_attr, dr, dg, db):
    for li, _, _vi in loop_map:
        r = max(0.0, min(1.0, 1.0 + dr[li]))
        g = max(0.0, min(1.0,       dg[li]))
        b = max(0.0, min(1.0,       db[li]))
        setattr(color_data[li], color_attr, (r, g, b, 1.0))


def _add_organic_blobs(loop_map, mesh, obj, dr, dg, db,
                       count, radius_frac, strength, channel,
                       diagonal, falloff, interior_mask):
    """Like _add_blobs but each blob is a randomly oriented ellipse rather than
    a circle, giving a more natural, less uniform appearance."""
    radius = diagonal * radius_frac
    for _ in range(count):
        centre     = _random_interior_point(mesh, obj, interior_mask)
        angle      = random.uniform(0, math.pi)
        axis       = Vector((math.cos(angle), math.sin(angle), 0.0))
        elongation = random.uniform(1.8, 4.0)   # stretch along the random axis
        jitter     = random.uniform(0.5, 1.0)

        for li, wco, vi in loop_map:
            if not interior_mask[vi]:
                continue
            diff       = wco - centre
            along      = diff.dot(axis)
            cross      = (diff - axis * along).length
            # Normalised elliptical distance: 0 at centre, 1 at edge
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


def _add_dry_patches(loop_map, mesh, obj, dr, db,
                     count, min_radius, max_radius,
                     min_strength, max_strength, falloff,
                     free_mask):
    """Scatter random elliptical dry (blue) patches in areas marked by
    free_mask.  Blob centres are biased toward higher elevation so patches
    cluster on ridges and rises rather than low flat areas."""
    mw = obj.matrix_world

    # Collect eligible polygon centres (all corner verts must be free).
    candidate_centres = []
    candidate_z       = []
    for poly in mesh.polygons:
        if not all(free_mask[mesh.loops[li].vertex_index]
                   for li in poly.loop_indices):
            continue
        wco = mw @ poly.center
        candidate_centres.append(wco)
        candidate_z.append(wco.z)

    if not candidate_centres:
        return

    # Elevation-weighted sampling: higher polys are more likely to host a blob,
    # but a 0.1 baseline keeps low areas occasionally reachable.
    z_min = min(candidate_z)
    z_rng = max(max(candidate_z) - z_min, 0.001)
    weights = [(z - z_min) / z_rng + 0.1 for z in candidate_z]

    for _ in range(count):
        centre     = random.choices(candidate_centres, weights=weights, k=1)[0]
        radius     = random.uniform(min_radius, max_radius)
        strength   = random.uniform(min_strength, max_strength)
        angle      = random.uniform(0, math.pi)
        axis       = Vector((math.cos(angle), math.sin(angle), 0.0))
        elongation = random.uniform(1.5, 3.5)

        for li, wco, vi in loop_map:
            if not free_mask[vi]:
                continue
            diff  = wco - centre
            along = diff.dot(axis)
            cross = (diff - axis * along).length
            t = math.sqrt((along / (radius * elongation)) ** 2 +
                          (cross / radius) ** 2)
            if t >= 1.0:
                continue
            w = math.exp(-falloff * t * t) * strength
            db[li] = max(db[li], w)
            dr[li] = min(dr[li], -w * 0.15)


def _height_gradient(loop_map, vert_noise, dr, dg, db,
                     dark_strength, blue_strength, blue_threshold=0.0):
    """Weight dark/blue influence by terrain elevation.
    Low vertices trend dark; high vertices trend blue (dry).
    blue_threshold (0–1): elevation fraction below which no blue is applied.
    vert_noise is a pre-generated per-vertex float list for natural variation."""
    z_vals  = [wco.z for _, wco, _ in loop_map]
    z_min   = min(z_vals)
    z_range = max(max(z_vals) - z_min, 0.001)

    for li, wco, vi in loop_map:
        t = (wco.z - z_min) / z_range      # 0 = lowest, 1 = highest
        n = vert_noise[vi]

        if dark_strength > 0:
            dark_w = (1.0 - t) * dark_strength * (0.3 + 0.7 * n)
            dr[li] = min(dr[li], -dark_w)
            dg[li] = min(dg[li], -dark_w)
            db[li] = min(db[li], -dark_w)

        if blue_strength > 0 and t > blue_threshold:
            # Smooth ramp from 0 at the threshold to full at t=1
            t_remapped = (t - blue_threshold) / (1.0 - blue_threshold)
            blue_w = t_remapped * blue_strength * (0.3 + 0.7 * n)
            db[li] = max(db[li], blue_w)
            dr[li] = min(dr[li], dr[li] - blue_w * 0.15)


def _build_material_kd(scene, obj, mat_names):
    """Build a KDTree of world-space vertex positions from every mesh in the
    scene whose material slots contain any of the given material names.
    Returns None if no matching meshes are found."""
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
    """Return (data, domain) for the best available colour layer on a mesh.
    Tries 'Col' by name first, then the active layer, then any layer.
    domain is 'POINT' (per-vertex) or 'CORNER' (per-loop)."""
    if hasattr(mesh, "color_attributes"):
        # Prefer the layer our tool wrote ("Col"), then active, then first
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



def _hash2(cx, cy):
    """Deterministic pseudo-random value (0–1) for an integer grid coordinate."""
    h = (cx * 1664525 + cy * 1013904223) & 0x7FFFFFFF
    return h / 0x7FFFFFFF


def _smooth_noise_2d(pos, scale):
    """Smooth value noise: bilinearly interpolates the hash at the four
    surrounding grid corners using a smoothstep curve.  Unlike the old
    _cell_value, there are no hard cell boundaries — values transition
    continuously, which prevents striping when mesh vertices align with
    the grid."""
    sx = pos.x / scale
    sy = pos.y / scale
    ix = int(math.floor(sx))
    iy = int(math.floor(sy))
    fx = sx - ix
    fy = sy - iy
    # Smoothstep (3t²−2t³) removes the linear kink at cell corners
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


def _apply_proximity_blue(loop_map, obj, scene, dr, db,
                          mat_names, radius, vert_noise,
                          strength, variability,
                          blotch=False, blotch_scale=3.0, blotch_coverage=0.55,
                          radius_jitter=0.0, radius_jitter_scale=6.0):
    """Adjust blue delta based on proximity to meshes with the given materials.
    Positive strength → add blue (dry effect near concrete/bunkers).
    Negative strength → subtract blue (lush/wet effect near water/fairway edges).
    variability (0–1): per-vertex noise modulation of strength.
    blotch: if True, use coarse cellular noise to leave some patches unpainted,
            creating red islands within the dry zone.
    blotch_scale: size in metres of each blotch cell.
    blotch_coverage: fraction of cells that receive the effect (rest stay red).
    radius_jitter (0–1): how much the effective radius can shrink below `radius`.
        0 = uniform edge, 0.5 = edge varies between 50 % and 100 % of radius.
    radius_jitter_scale: spatial scale in metres of the radius variation noise."""
    kd = _build_material_kd(scene, obj, mat_names)
    if kd is None:
        return

    for li, wco, vi in loop_map:
        _, _, dist = kd.find(wco)
        if dist >= radius:
            continue

        # Optionally shrink the effective radius per-position using smooth noise,
        # so the influence edge is irregular rather than a uniform band.
        if radius_jitter > 0.0:
            noise_val        = _smooth_noise_2d(wco, radius_jitter_scale)
            effective_radius = radius * (1.0 - radius_jitter * noise_val)
        else:
            effective_radius = radius

        if dist >= effective_radius:
            continue

        # Quadratic falloff → much softer transition at the far edge
        t         = dist / effective_radius
        proximity = 1.0 - t * t

        n          = vert_noise[vi]
        var_factor = (1.0 - variability) + variability * n

        if blotch:
            cell_val   = _smooth_noise_2d(wco, blotch_scale)
            blotch_val = cell_val * 0.65 + n * 0.35
            if blotch_val < (1.0 - blotch_coverage):
                continue                     # this cell stays red
            var_factor *= blotch_val         # stronger in well-active cells

        w = proximity * abs(strength) * var_factor

        if strength > 0:
            db[li] = max(db[li], w)
            dr[li] = min(dr[li], -w * 0.15)
        else:
            db[li] = max(0.0, db[li] - w)


def _apply_tee_seam_red(obj, loop_map, color_data, color_attr, radius=5.0):
    """Blend rough vertices near 'Tee - Blend' meshes toward pure red with
    a smoothstep falloff, producing the same clean red edge the deep rough
    painter creates at its own mesh boundary."""
    kd = _build_material_kd(bpy.context.scene, obj, {"Tee - Blend"})
    if kd is None:
        return
    for li, wco, _ in loop_map:
        _, _, dist = kd.find(wco)
        if dist >= radius:
            continue
        t     = dist / radius
        ts    = t * t * (3.0 - 2.0 * t)   # smoothstep: 0 at seam, 1 at radius
        blend = 1.0 - ts                    # 1 = full red at seam, 0 = no change at far edge
        cur   = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        setattr(color_data[li], color_attr,
                (min(1.0, r + (1.0 - r) * blend),
                 g * (1.0 - blend),
                 b * (1.0 - blend),
                 a))


def _apply_rough_seam_blend(obj, loop_map, color_data, color_attr, scene,
                            seam_radius=5.0):
    """After rough is painted, blend boundary vertices toward adjacent
    already-painted rough meshes so seams between neighbouring sections
    disappear.  Bunkers and Tee-Blend meshes are excluded — they have their
    own dedicated seam handling."""
    mw   = obj.matrix_world
    mesh = obj.data

    # Exclude Bunker and Tee-Blend (own painters) and Concrete-Blend (cart paths
    # have blue=0 which would zero out the dryness we painted along their edges)
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
            weight = 1.0 - t * t * (3.0 - 2.0 * t)   # smoothstep → 0
            target = b_blues[bidx]

        cur = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        # delta can be positive (add blue) or negative (reduce blue to match)
        delta = (target - b) * weight
        setattr(color_data[li], color_attr,
                (max(0.0, min(1.0, r - delta)), g,
                 max(0.0, min(1.0, b + delta)), a))


def _find_adjacent_objects(scene, obj, mat_names):
    """Return all scene objects whose material matches any of mat_names AND
    that share at least one colocated vertex (within 0.05 m) with obj's open
    boundary.  Uses the same colocated-vertex approach as _sample_neighbor_blues."""
    mw   = obj.matrix_world
    mesh = obj.data

    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_vis = {v.index for e in bm.edges if e.is_boundary for v in e.verts}
    bm.free()

    if not boundary_vis:
        return []

    b_positions = [mw @ mesh.vertices[vi].co for vi in boundary_vis]
    b_kd = KDTree(len(b_positions))
    for i, pos in enumerate(b_positions):
        b_kd.insert(pos, i)
    b_kd.balance()

    bx = [p.x for p in b_positions]
    by = [p.y for p in b_positions]
    bbox_min_x, bbox_max_x = min(bx) - 1.0, max(bx) + 1.0
    bbox_min_y, bbox_max_y = min(by) - 1.0, max(by) + 1.0

    result = []
    for other in scene.objects:
        if other is obj or other.type != "MESH":
            continue
        other_mats = {s.material.name for s in other.material_slots if s.material}
        if not (mat_names & other_mats):
            continue
        oc = [other.matrix_world @ Vector(c) for c in other.bound_box]
        if (max(c.x for c in oc) < bbox_min_x or min(c.x for c in oc) > bbox_max_x or
                max(c.y for c in oc) < bbox_min_y or min(c.y for c in oc) > bbox_max_y):
            continue
        if any(b_kd.find(other.matrix_world @ v.co)[2] < 0.05
               for v in other.data.vertices):
            result.append(other)
    return result


# ══════════════════════════════════════════════════════════════════════════════
# TEE BOXES
# Base color: Red (R=1, G=0, B=0)
# R channel = base tee surface
# G channel = divots  (par 3 only)
# B channel = dry / sun-bleached patches
# Darkness  = reduce all channels equally
# ══════════════════════════════════════════════════════════════════════════════

def _paint_tee(obj, seed, include_divots):
    mesh = obj.data
    if seed >= 0:
        random.seed(seed)

    color_data, color_attr = _ensure_color_layer(mesh)
    diag     = _mesh_diagonal(obj)
    loop_map = _build_loop_map(mesh, obj)
    interior = _build_interior_mask(mesh, obj, margin_frac=0.10)
    n        = len(mesh.loops)

    for li, _, _vi in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    dr = [0.0] * n
    dg = [0.0] * n
    db = [0.0] * n

    _add_blobs(loop_map, mesh, obj, dr, dg, db,
               count=2, radius_frac=0.08, strength=0.18,
               channel="dark", diagonal=diag, falloff=3.5, interior_mask=interior)

    _add_blobs(loop_map, mesh, obj, dr, dg, db,
               count=2, radius_frac=0.10, strength=0.06,
               channel="blue", diagonal=diag, falloff=3.0, interior_mask=interior)

    if include_divots:
        _add_spaced_blobs(loop_map, mesh, obj, dr, dg, db,
                          min_separation=10.0, radius_frac=0.10, strength=1.0,
                          channel="green", diagonal=diag, falloff=4.0,
                          interior_mask=interior)

    _apply_deltas(loop_map, color_data, color_attr, dr, dg, db)
    mesh.update()

    # Auto-paint any adjacent Tee - Blend transition meshes.
    for blend_obj in _find_adjacent_objects(
            bpy.context.scene, obj, {"Tee - Blend"}):
        paint_tee_blend(blend_obj)


def paint_tee_blend(obj):
    """Copy blue from surrounding rough into a Tee - Blend transition mesh.
    The blend has two boundary loops: the inner ring (touching the tee box)
    picks up tee-box blue; the outer ring (touching rough) picks up rough blue.
    Interior vertices get a falloff blend between the two.
    Repainting is safe: any blue from a previous run is wiped first."""
    mesh = obj.data
    mw   = obj.matrix_world

    color_data, color_attr = _ensure_color_layer(mesh)
    loop_map = _build_loop_map(mesh, obj)

    # ── Step 0: flood-fill red ────────────────────────────────────────────────
    # Resets any prior paint (black edges, old blue, etc.) to a clean base.
    for li, _, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    # ── Step 1: find boundary vertices and separate into inner / outer loops ──
    bm_tmp = bmesh.new()
    bm_tmp.from_mesh(mesh)
    boundary_vis = {v.index for e in bm_tmp.edges if e.is_boundary for v in e.verts}

    # Build adjacency for boundary verts so we can walk the two ring loops
    b_adj = {}
    for e in bm_tmp.edges:
        if e.is_boundary:
            i0, i1 = e.verts[0].index, e.verts[1].index
            b_adj.setdefault(i0, []).append(i1)
            b_adj.setdefault(i1, []).append(i0)
    bm_tmp.free()

    # Walk connected components to collect each separate boundary loop
    loops      = []
    visited_b  = set()
    for start in b_adj:
        if start in visited_b:
            continue
        loop, prev, curr = [], None, start
        while True:
            loop.append(curr)
            visited_b.add(curr)
            nbrs = [n for n in b_adj[curr] if n != prev]
            if not nbrs or nbrs[0] in visited_b:
                break
            prev, curr = curr, nbrs[0]
        loops.append(set(loop))

    # Inner loop = shorter perimeter (smaller ring touching the tee box)
    inner_loop = set()
    if len(loops) >= 2:
        def _perim(lp):
            tot = 0.0
            for vi in lp:
                wco = mw @ mesh.vertices[vi].co
                for nb in b_adj.get(vi, []):
                    if nb in lp:
                        tot += (wco - mw @ mesh.vertices[nb].co).length
            return tot / 2.0
        perims     = [_perim(lp) for lp in loops]
        inner_loop = loops[perims.index(min(perims))]

    # Paint inner loop verts black; Step 5's r < 0.4 guard keeps them that way
    for li, _, vi in loop_map:
        if vi in inner_loop:
            setattr(color_data[li], color_attr, (0.0, 0.0, 0.0, 1.0))

    # ── Step 2: sample blues from all attached non-Tee-Blend neighbours ───────
    # This includes both the tee box (inner boundary → low blue) and the rough
    # (outer boundary → higher blue), giving a natural gradient across the ring.
    kd, neighbor_blues = _sample_neighbor_blues(
        bpy.context.scene, obj, exclude_mats={"Tee - Blend"})
    if kd is None:
        return

    # ── Step 3: match boundary verts to nearest neighbour ────────────────────
    boundary_blue = {}
    for vi in boundary_vis:
        wco = mw @ mesh.vertices[vi].co
        _, idx, dist = kd.find(wco)
        if dist < 1.0:
            boundary_blue[vi] = neighbor_blues[idx]

    if not boundary_blue:
        return

    # ── Step 4: build falloff KDTree from matched boundary positions ──────────
    b_positions = [mw @ mesh.vertices[vi].co for vi in boundary_blue]
    b_blues     = list(boundary_blue.values())
    bkd = KDTree(len(b_positions))
    for i, pos in enumerate(b_positions):
        bkd.insert(pos, i)
    bkd.balance()

    FALLOFF = 8.0

    # ── Step 5: apply — red vertices only ────────────────────────────────────
    for li, wco, vi in loop_map:
        cur = getattr(color_data[li], color_attr)
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        if r < 0.4 or g > 0.25 or b > 0.25:
            continue

        if vi in boundary_blue:
            target = boundary_blue[vi]
        else:
            _, bidx, bdist = bkd.find(wco)
            if bdist >= FALLOFF:
                continue
            target = b_blues[bidx] * (1.0 - (bdist / FALLOFF) ** 2)

        if target < 0.001:
            continue

        setattr(color_data[li], color_attr,
                (max(0.0, r - target), g, min(1.0, max(b, target)), a))

    mesh.update()


def paint_tee_par3(obj, seed):
    _paint_tee(obj, seed, include_divots=True)


def paint_tee_par45(obj, seed):
    _paint_tee(obj, seed, include_divots=False)


# ── Tee operators ─────────────────────────────────────────────────────────────

class CVR_OT_paint_tee_par3(bpy.types.Operator):
    bl_idname      = "cvr.paint_tee_par3"
    bl_label       = "Par 3 Tee"
    bl_description = ("Vertex paint selected meshes as a Par 3 tee "
                      "(red base + dark spots + dry patches + divots)")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvr_props
        seed    = props.seed if props.use_seed else -1
        painted = sum(1 for obj in context.selected_objects
                      if obj.type == "MESH" and not paint_tee_par3(obj, seed))
        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Par 3 paint applied to {painted} object(s).")
        return {"FINISHED"}


class CVR_OT_paint_tee_par45(bpy.types.Operator):
    bl_idname      = "cvr.paint_tee_par45"
    bl_label       = "Par 4 / 5 Tee"
    bl_description = ("Vertex paint selected meshes as a Par 4/5 tee "
                      "(red base + dark spots + dry patches, no divots)")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvr_props
        seed    = props.seed if props.use_seed else -1
        painted = sum(1 for obj in context.selected_objects
                      if obj.type == "MESH" and not paint_tee_par45(obj, seed))
        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Par 4/5 paint applied to {painted} object(s).")
        return {"FINISHED"}


# ── Tee panel ─────────────────────────────────────────────────────────────────

class CVR_PT_tee(bpy.types.Panel):
    bl_label       = "Tee Boxes"
    bl_idname      = "CVR_PT_tee"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Course Vertex Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.cvr_props

        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvr.paint_tee_par3",  icon="NODE_MATERIAL")
        col.operator("cvr.paint_tee_par45", icon="NODE_MATERIAL")

        layout.separator()
        layout.label(text="Paint rough first — Tee-Blend", icon="INFO")
        layout.label(text="derives color from neighbors.")

        layout.separator()
        box = layout.box()
        box.label(text="Options", icon="SETTINGS")
        row = box.row()
        row.prop(props, "use_seed", text="Fixed Seed")
        sub = row.row()
        sub.enabled = props.use_seed
        sub.prop(props, "seed", text="")


# ══════════════════════════════════════════════════════════════════════════════
# FAIRWAYS
# Base color: Red (R=1, G=0, B=0)
# R channel = base fairway surface
# B channel = dry patches  (preferred on higher ground)
# Darkness  = reduce all channels  (preferred on lower ground)
# No green channel (no divots on fairways)
# Green-Blend adjacency: 2 m strip of pure red where fairway meets putting surface
# ══════════════════════════════════════════════════════════════════════════════

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
            weight = 1.0 - t * t * (3.0 - 2.0 * t)   # smoothstep → 0 at edge
        new_r = r * (1.0 - weight)
        new_g = g * (1.0 - weight)
        setattr(color_data[li], color_attr, (new_r, new_g, b, a))


def _boundary_one_side_distances(mesh, obj):
    """Walk the open boundary chain, take the first half (one long side), and
    return per-vertex perpendicular distance to the nearest segment on that half.
    Segment distances are smooth and monotonic from edge to edge — no Voronoi
    artifacts — and naturally follow the fairway's curve."""
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
    curve.  One-sided perpendicular boundary distance gives a monotonic stripe
    coordinate.  Sine-based blending eliminates jagged sawtooth edges — each
    vertex gets a continuous R/G ratio rather than a hard flip.
    Blue channel (dry splotches) is preserved.  Dark apron verts are skipped."""
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

        # Sine blend: t=0 full red, t=1 full green, smooth gradient at boundaries
        t = 0.5 + 0.5 * math.sin(coord / stripe_period * 2.0 * math.pi)
        setattr(color_data[li], color_attr, (1.0 - t, t, b, a))


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

    # Per-vertex noise for the height gradient (generated after seeding so it's
    # reproducible when a fixed seed is used)
    vert_noise = [random.random() for _ in range(len(mesh.vertices))]

    # No boundary avoidance for fairways
    interior = [True] * len(mesh.vertices)

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


# ── Fairway operator ──────────────────────────────────────────────────────────

class CVR_OT_paint_fairway(bpy.types.Operator):
    bl_idname      = "cvr.paint_fairway"
    bl_label       = "Fairway"
    bl_description = ("Vertex paint selected meshes as a fairway — height-driven "
                      "dark (low) / dry (high) variation, clean seam at Green-Blend edges")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvr_props
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


# ── Fairway panel ─────────────────────────────────────────────────────────────

class CVR_PT_fairway(bpy.types.Panel):
    bl_label       = "Fairways"
    bl_idname      = "CVR_PT_fairway"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Course Vertex Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.cvr_props

        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvr.paint_fairway", icon="NODE_MATERIAL")

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
# ROUGH
# Base color: Red (R=1, G=0, B=0)
# B channel = dry patches (high ground, or near concrete/bunkers)
# No green channel (no divots)
# Wet push  : vertices near "Semi - Blend" or "Water_Base_Lake" → reduce blue
# Dry push  : vertices near "Concrete - Blend" or "Bunker" → add blue (high variability)
# ══════════════════════════════════════════════════════════════════════════════

WET_MATS = {"Semi - Blend", "Water_Base_Lake"}
DRY_MATS = {"Concrete - Blend", "Bunker"}
FAIRWAY_EDGE_MATS = {"Semi - Blend"}


def _add_lush_patches(loop_map, mesh, obj, db, scene, mow_depth,
                      count=10, min_radius=4.0, max_radius=12.0,
                      strength=0.30, falloff=2.0, patch_zone=25.0):
    """Scatter organic elliptical green patches in the dry zone beyond mow_depth.
    Reduces the blue channel inside each blob so they read as lush islands
    against the otherwise uniform dry background.
    patch_zone: how many metres beyond mow_depth patch centres can spawn —
    keeps patches in the near-fairway playing area, not across a huge mesh."""
    kd_fw = _build_material_kd(scene, obj, FAIRWAY_EDGE_MATS)
    mw    = obj.matrix_world

    max_d = mow_depth + patch_zone

    beyond_centres = []
    for poly in mesh.polygons:
        wco = mw @ poly.center
        d   = kd_fw.find(wco)[2] if kd_fw else 0.0
        if mow_depth <= d <= max_d:
            beyond_centres.append(wco)

    if not beyond_centres:
        return

    for _ in range(count):
        centre     = random.choice(beyond_centres)
        radius     = random.uniform(min_radius, max_radius)
        strength_v = random.uniform(strength * 0.5, strength)
        angle      = random.uniform(0, math.pi)
        axis       = Vector((math.cos(angle), math.sin(angle), 0.0))
        elongation = random.uniform(1.5, 3.5)

        for li, wco, vi in loop_map:
            diff  = wco - centre
            along = diff.dot(axis)
            cross = (diff - axis * along).length
            t = math.sqrt((along / (radius * elongation)) ** 2 +
                          (cross / radius) ** 2)
            if t >= 1.0:
                continue
            w = math.exp(-falloff * t * t) * strength_v
            db[li] = max(0.0, db[li] - w)


def _apply_mowline_stripes(loop_map, obj, scene, dr, dg, db,
                           mow_depth=15.0, stripe_period=2.5,
                           strength=0.10, noise_warp=0.4,
                           beyond_strength=0.0):
    """Paint fairway-parallel mow-line stripes by modulating the blue channel
    with a sine wave keyed on each vertex's distance to Semi-Blend meshes.
    Iso-distance curves naturally follow the fairway edge profile.
    Only the positive (light-stripe) half of each cycle adds blue; dark-stripe
    halves leave the existing color untouched."""
    kd = _build_material_kd(scene, obj, FAIRWAY_EDGE_MATS)
    if kd is None:
        return

    NOISE_SCALE = 7.0  # metres — spatial period of phase undulation

    for li, wco, vi in loop_map:
        _, _, d_fw = kd.find(wco)
        if d_fw >= mow_depth:
            if beyond_strength > 0.0:
                # Noise-varied blend zone 1–15 m wide so the transition from
                # mow lines to full dry is organic rather than a hard ring.
                # A separate noise offset decorrelates this from the stripe phase.
                n_blend = _smooth_noise_2d(
                    Vector((wco.x + 37.5, wco.y + 61.3, wco.z)), 8.0)
                blend_w  = 1.0 + n_blend * 14.0   # 1 m to 15 m per location
                d_beyond = d_fw - mow_depth
                if d_beyond >= blend_w:
                    t = 1.0
                else:
                    t_raw = d_beyond / blend_w
                    t = t_raw * t_raw * (3.0 - 2.0 * t_raw)   # smoothstep
                blue_val = t * beyond_strength
                db[li] = max(db[li], blue_val)
                dr[li] = min(dr[li], -blue_val * 0.15)
            continue

        # Gentle power fade: stripes stay readable across the full mow depth.
        # Exponent 0.7 decays much slower than quadratic so outer stripes survive.
        fade = (1.0 - (d_fw / mow_depth)) ** 0.7

        # Phase noise shifts the stripe boundary so lines gently undulate
        n = _smooth_noise_2d(wco, NOISE_SCALE)
        phase_offset = (n - 0.5) * noise_warp * math.pi

        phase = (d_fw / stripe_period) * 2.0 * math.pi + phase_offset
        sine_val = math.sin(phase)

        if sine_val > 0.0:
            # Light stripe: push toward dry/bleached (add blue, reduce red)
            blue_delta = sine_val * strength * fade
            db[li] = max(db[li], blue_delta)
            dr[li] = min(dr[li], -blue_delta * 0.15)
        else:
            # Dark stripe: push slightly darker to widen the contrast swing
            dark_delta = (-sine_val) * strength * 0.4 * fade
            dr[li] = min(dr[li], -dark_delta)
            dg[li] = min(dg[li], -dark_delta)


def paint_rough(obj, seed, tee_seam_radius=5.0):
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

    # Height gradient: blue on higher ground (rough dries out on rises)
    _height_gradient(loop_map, vert_noise, dr, dg, db,
                     dark_strength=0.0, blue_strength=0.05, blue_threshold=0.4)

    # Organic blobs for baseline variety
    _add_organic_blobs(loop_map, mesh, obj, dr, dg, db,
                       count=3, radius_frac=0.10, strength=0.04,
                       channel="blue", diagonal=diag, falloff=2.5, interior_mask=interior)

    # Wet push: near fairway edges and water → healthier, greener, less blue.
    # radius_jitter lets the edge vary between 3.5 m and 7 m so it reads as
    # natural sprinkler coverage rather than a uniform band.
    _apply_proximity_blue(loop_map, obj, bpy.context.scene, dr, db,
                          mat_names=WET_MATS, radius=7.0,
                          vert_noise=vert_noise, strength=-0.08, variability=0.25,
                          radius_jitter=0.5, radius_jitter_scale=6.0)

    # Dry push: near cart paths and bunkers → stressed grass, blotchy blue islands
    _apply_proximity_blue(loop_map, obj, bpy.context.scene, dr, db,
                          mat_names=DRY_MATS, radius=5.0,
                          vert_noise=vert_noise, strength=0.14, variability=0.75,
                          blotch=True, blotch_scale=3.5, blotch_coverage=0.55)

    # Random dry patches: scattered through open areas, biased toward high ground,
    # staying clear of cart paths/bunkers (8 m) and fairway/water edges (10 m).
    mw = obj.matrix_world
    kd_dry_p = _build_material_kd(bpy.context.scene, obj, DRY_MATS)
    kd_wet_p = _build_material_kd(bpy.context.scene, obj, WET_MATS)
    free_mask = []
    for vert in mesh.vertices:
        wco  = mw @ vert.co
        near = ((kd_dry_p and kd_dry_p.find(wco)[2] < 8.0) or
                (kd_wet_p and kd_wet_p.find(wco)[2] < 10.0))
        free_mask.append(not near)

    _add_dry_patches(loop_map, mesh, obj, dr, db,
                     count=10,
                     min_radius=5.0,  max_radius=15.0,
                     min_strength=0.16, max_strength=0.26,
                     falloff=2.0,
                     free_mask=free_mask)

    _apply_deltas(loop_map, color_data, color_attr, dr, dg, db)
    _apply_rough_seam_blend(obj, loop_map, color_data, color_attr, bpy.context.scene)
    if tee_seam_radius > 0.0:
        _apply_tee_seam_red(obj, loop_map, color_data, color_attr, tee_seam_radius)
    mesh.update()


def paint_rough_mowlines(obj, seed,
                         mow_depth=15.0, stripe_period=2.5,
                         stripe_strength=0.10, beyond_strength=0.45,
                         lush_patch_count=10, lush_patch_zone=25.0,
                         tee_seam_radius=5.0):
    """Rough type 2: subtle fairway-parallel mow stripes within mow_depth, then
    height-varied dry blue beyond it broken up by organic lush patches and small
    concentrated dry blobs."""
    mesh = obj.data
    if seed >= 0:
        random.seed(seed)

    color_data, color_attr = _ensure_color_layer(mesh)
    loop_map = _build_loop_map(mesh, obj)
    n        = len(mesh.loops)
    mw       = obj.matrix_world

    vert_noise = [random.random() for _ in range(len(mesh.vertices))]

    # ── Local height score: 0 = lowest, 1 = highest within 10 m ─────────────
    # Used to scale the beyond-zone dry blue so ridges are drier than hollows.
    all_wcos = [mw @ v.co for v in mesh.vertices]
    v_kd = KDTree(len(all_wcos))
    for i, wco_v in enumerate(all_wcos):
        v_kd.insert(wco_v, i)
    v_kd.balance()
    local_height = []
    for wco_v in all_wcos:
        nbrs   = v_kd.find_range(wco_v, 10.0)
        z_vals = [all_wcos[idx].z for _, idx, _ in nbrs]
        if len(z_vals) < 2:
            local_height.append(0.5)
            continue
        z_min, z_max = min(z_vals), max(z_vals)
        z_rng = z_max - z_min
        if z_rng < 0.01:
            local_height.append(0.5)
            continue
        local_height.append((wco_v.z - z_min) / z_rng)

    # ── Pre-compute per-vertex fairway distance (reused for height scaling,
    #    beyond_mask, and lush patches) ────────────────────────────────────────
    kd_fw    = _build_material_kd(bpy.context.scene, obj, FAIRWAY_EDGE_MATS)
    vert_dfw = [kd_fw.find(wco_v)[2] if kd_fw else 0.0 for wco_v in all_wcos]

    # Flood-fill red
    for li, _, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    dr = [0.0] * n
    dg = [0.0] * n
    db = [0.0] * n

    # Very subtle height tint so terrain reads on high ground
    _height_gradient(loop_map, vert_noise, dr, dg, db,
                     dark_strength=0.0, blue_strength=0.03, blue_threshold=0.5)

    # Subtle mow stripes near fairway; variable dry blue beyond mow_depth
    _apply_mowline_stripes(loop_map, obj, bpy.context.scene, dr, dg, db,
                           mow_depth=mow_depth, stripe_period=stripe_period,
                           strength=stripe_strength, beyond_strength=beyond_strength)

    # Scale beyond-zone blue by local height: high ridges get full beyond_strength,
    # low hollows scale toward 0 — same idea as _height_gradient but local (10 m).
    for li, wco, vi in loop_map:
        if db[li] > 0.0 and vert_dfw[vi] >= mow_depth:
            db[li] *= local_height[vi]

    # Organic green patches punched into the dry zone
    _add_lush_patches(loop_map, mesh, obj, db, bpy.context.scene, mow_depth,
                      count=lush_patch_count, patch_zone=lush_patch_zone)

    # Small concentrated dry blobs scattered through the playable beyond zone —
    # push some spots noticeably drier than the height-based baseline.
    beyond_mask = [mow_depth <= d <= mow_depth + lush_patch_zone for d in vert_dfw]
    _add_dry_patches(loop_map, mesh, obj, dr, db,
                     count=10,
                     min_radius=2.0, max_radius=6.0,
                     min_strength=0.25, max_strength=0.40,
                     falloff=2.5,
                     free_mask=beyond_mask)

    # Tight dry band right along cart paths — strength must exceed beyond_strength
    # (0.45) to be visible; narrow radius keeps effect to the first vertex ring only.
    _apply_proximity_blue(loop_map, obj, bpy.context.scene, dr, db,
                          mat_names={"Concrete - Blend"}, radius=0.5,
                          vert_noise=vert_noise, strength=0.65, variability=0.25)

    # Wider dry push near bunkers (blotchy, same as standard rough but stronger
    # so it reads above the mow-line dry baseline)
    _apply_proximity_blue(loop_map, obj, bpy.context.scene, dr, db,
                          mat_names={"Bunker"}, radius=5.0,
                          vert_noise=vert_noise, strength=0.60, variability=0.75,
                          blotch=True, blotch_scale=3.5, blotch_coverage=0.55)

    _apply_deltas(loop_map, color_data, color_attr, dr, dg, db)
    _apply_rough_seam_blend(obj, loop_map, color_data, color_attr, bpy.context.scene)
    if tee_seam_radius > 0.0:
        _apply_tee_seam_red(obj, loop_map, color_data, color_attr, tee_seam_radius)
    mesh.update()


# ── Rough operator ────────────────────────────────────────────────────────────

class CVR_OT_paint_rough(bpy.types.Operator):
    bl_idname      = "cvr.paint_rough"
    bl_label       = "Rough"
    bl_description = ("Vertex paint selected meshes as rough — height-driven dryness, "
                      "lush near water/fairway edges, stressed near paths and bunkers")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvr_props
        seed    = props.seed if props.use_seed else -1
        painted = 0
        for obj in context.selected_objects:
            if obj.type == "MESH":
                if props.rough_type == "TYPE2":
                    paint_rough_mowlines(obj, seed,
                                         mow_depth=props.rough_mow_depth,
                                         stripe_period=props.rough_mow_period,
                                         stripe_strength=props.rough_stripe_strength,
                                         beyond_strength=props.rough_mow_strength,
                                         lush_patch_count=props.rough_lush_patch_count,
                                         lush_patch_zone=props.rough_lush_patch_zone,
                                         tee_seam_radius=props.rough_tee_seam_radius)
                else:
                    paint_rough(obj, seed,
                                tee_seam_radius=props.rough_tee_seam_radius)
                painted += 1
        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Rough paint applied to {painted} object(s).")
        return {"FINISHED"}


# ── Rough panel ───────────────────────────────────────────────────────────────

class CVR_PT_rough(bpy.types.Panel):
    bl_label       = "Rough"
    bl_idname      = "CVR_PT_rough"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Course Vertex Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.cvr_props

        box = layout.box()
        box.label(text="Rough Type", icon="SETTINGS")
        box.prop(props, "rough_type", expand=True)

        if props.rough_type == "TYPE2":
            layout.separator()
            mbox = layout.box()
            mbox.label(text="Mow Zone", icon="MOD_ARRAY")
            mbox.prop(props, "rough_mow_depth",      text="Depth (m)")
            mbox.prop(props, "rough_mow_period",     text="Stripe Width (m)")
            mbox.prop(props, "rough_stripe_strength", text="Stripe Intensity")
            layout.separator()
            dbox = layout.box()
            dbox.label(text="Dry Zone (beyond depth)", icon="OUTLINER_OB_SURFACE")
            dbox.prop(props, "rough_mow_strength",     text="Dry Blue")
            dbox.prop(props, "rough_lush_patch_count", text="Green Patches")
            dbox.prop(props, "rough_lush_patch_zone",  text="Patch Zone (m)")

        layout.separator()
        box = layout.box()
        box.label(text="Tee Seam", icon="ORIENTATION_LOCAL")
        box.prop(props, "rough_tee_seam_radius", text="Red Band Radius (m)")
        box.label(text="Requires Tee-Blend mesh in scene.", icon="INFO")

        layout.separator()
        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvr.paint_rough", icon="NODE_MATERIAL")

        layout.separator()
        box = layout.box()
        box.label(text="Options", icon="SETTINGS")
        row = box.row()
        row.prop(props, "use_seed", text="Fixed Seed")
        sub = row.row()
        sub.enabled = props.use_seed
        sub.prop(props, "seed", text="")


# ══════════════════════════════════════════════════════════════════════════════
# BUNKERS
# Does NOT flood-fill. Reads existing vertex colors and shifts red → blue
# proportionally to the blue level found in adjacent grass/path meshes.
# Green channel and overall darkness (black) are left completely untouched.
# ══════════════════════════════════════════════════════════════════════════════

def _sample_neighbor_blues(scene, obj, exclude_mats=None):
    """Build a KDTree of vertex positions/blues from every mesh that is
    geometrically attached to obj's open boundary — i.e. shares at least one
    colocated vertex (within 0.05 m).  Meshes whose material is in
    exclude_mats are skipped (defaults to {"Bunker"})."""
    if exclude_mats is None:
        exclude_mats = {"Bunker"}
    mw   = obj.matrix_world
    mesh = obj.data

    # Build a KDTree from obj's own boundary vertex positions.
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

    # XY bounding box of the boundary ring (+ 1 m buffer) for a cheap pre-filter
    # that avoids the vertex-by-vertex check on obviously distant meshes.
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

        # Cheap XY bbox pre-filter.
        oc = [other.matrix_world @ Vector(c) for c in other.bound_box]
        if (max(c.x for c in oc) < bbox_min_x or min(c.x for c in oc) > bbox_max_x or
                max(c.y for c in oc) < bbox_min_y or min(c.y for c in oc) > bbox_max_y):
            continue

        omesh = other.data
        omw   = other.matrix_world
        cd, dom = _read_color_layer(omesh)
        if cd is None:
            continue

        # Exact adjacency: stop as soon as one colocated vertex is found.
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

        mesh_max = max(vb[vi] / vc[vi] for vi in vb)
        print(f"[bunker_sample] ATTACHED {other.name}: {len(vb)} verts, "
              f"max_blue={mesh_max:.4f}, domain={dom}")

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


def paint_bunker(obj):
    """Match bunker edge colours to adjacent rough by exact position lookup
    (boundary vertices are colocated), then blend inward with falloff."""
    mesh = obj.data
    mw   = obj.matrix_world

    color_data, domain = _read_color_layer(mesh)
    if color_data is None:
        return

    loop_map = _build_loop_map(mesh, obj)

    # ── Step 0: clear blue from any previous bunker paint ────────────────────
    # Bunkers should only have red, green, and black. Any blue on a red vertex
    # was added by a previous run of this recipe; restore it to red so the
    # repainted rough is sampled cleanly with no stale colour underneath.
    for li, wco, vi in loop_map:
        read_idx = vi if domain == "POINT" else li
        cur = color_data[read_idx].color
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]
        if b > 0.001 and r >= 0.4 and g <= 0.25:
            color_data[read_idx].color = (min(1.0, r + b), g, 0.0, a)

    # ── Step 1: find bunker boundary vertex indices ───────────────────────────
    bm_tmp = bmesh.new()
    bm_tmp.from_mesh(mesh)
    boundary_vis = {v.index for e in bm_tmp.edges if e.is_boundary for v in e.verts}
    bm_tmp.free()

    # ── Step 2: build neighbour KDTree ────────────────────────────────────────
    kd, neighbor_blues = _sample_neighbor_blues(bpy.context.scene, obj)
    if kd is None:
        return

    # ── Step 3: match boundary verts to nearest rough vert ───────────────────
    # Rough meshes can be sparser than the bunker boundary ring, so use 1 m
    # to reliably find the closest neighbour even when vertex density differs.
    boundary_blue = {}   # vi → blue sampled from rough
    for vi in boundary_vis:
        wco = mw @ mesh.vertices[vi].co
        _, idx, dist = kd.find(wco)
        if dist < 1.0:
            boundary_blue[vi] = neighbor_blues[idx]

    if not boundary_blue:
        return

    # ── Step 4: build a KDTree of boundary positions for interior falloff ─────
    b_positions = [mw @ mesh.vertices[vi].co for vi in boundary_blue]
    b_blues     = list(boundary_blue.values())
    bkd = KDTree(len(b_positions))
    for i, pos in enumerate(b_positions):
        bkd.insert(pos, i)
    bkd.balance()

    FALLOFF = 8.0   # metres — blue fades to zero this far inside the bunker

    # ── Step 5: apply ─────────────────────────────────────────────────────────
    for li, wco, vi in loop_map:
        if vi in boundary_blue:
            target = boundary_blue[vi]
        else:
            _, bidx, bdist = bkd.find(wco)
            if bdist >= FALLOFF:
                continue
            target = b_blues[bidx] * (1.0 - (bdist / FALLOFF) ** 2)

        if target < 0.001:
            continue

        read_idx = vi if domain == "POINT" else li
        cur = color_data[read_idx].color
        r, g, b, a = cur[0], cur[1], cur[2], cur[3]

        # Only shift vertices that are predominantly red.
        # Dark sand (black) and green-edged verts are left completely alone.
        if r < 0.4 or g > 0.25 or b > 0.25:
            continue

        color_data[read_idx].color = (max(0.0, r - target), g,
                                      min(1.0, max(b, target)), a)

    mesh.update()


# ── Bunker operator ───────────────────────────────────────────────────────────

class CVR_OT_paint_bunker(bpy.types.Operator):
    bl_idname      = "cvr.paint_bunker"
    bl_label       = "Bunker"
    bl_description = ("Shift bunker vertex colors from red towards blue based on "
                      "the blue level of adjacent grass meshes. G and darkness untouched.")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        painted = 0
        for obj in context.selected_objects:
            if obj.type != "MESH":
                continue

            cd, _ = _read_color_layer(obj.data)
            if cd is None:
                self.report({"ERROR"}, f"{obj.name}: no vertex colour layer found on bunker")
                return {"CANCELLED"}

            print(f"\n=== BUNKER DIAGNOSTIC: {obj.name} ===")
            kd, blues = _sample_neighbor_blues(bpy.context.scene, obj)
            if kd is None:
                self.report({"ERROR"}, f"{obj.name}: no painted neighbour meshes found in scene")
                return {"CANCELLED"}

            max_b = max(blues) if blues else 0.0

            bm_d = bmesh.new()
            bm_d.from_mesh(obj.data)
            boundary_vis_d = {v.index for e in bm_d.edges if e.is_boundary for v in e.verts}
            n_boundary     = sum(1 for e in bm_d.edges if e.is_boundary)
            bm_d.free()

            mw = obj.matrix_world
            matched_blues = []
            unmatched = 0
            for vi in boundary_vis_d:
                wco = mw @ obj.data.vertices[vi].co
                _, idx, dist = kd.find(wco)
                if dist < 1.0:
                    matched_blues.append(blues[idx])
                else:
                    unmatched += 1

            match_max = max(matched_blues) if matched_blues else 0.0
            match_avg = (sum(matched_blues) / len(matched_blues)) if matched_blues else 0.0
            print(f"  boundary_edges={n_boundary}, boundary_verts={len(boundary_vis_d)}, "
                  f"matched={len(matched_blues)}, unmatched={unmatched}, "
                  f"match_max={match_max:.4f}, match_avg={match_avg:.4f}")
            self.report({"INFO"},
                f"{obj.name}: {len(blues)} nbr verts, nbr_max={max_b:.4f}, "
                f"bdry_edges={n_boundary}, matched={len(matched_blues)}/{len(boundary_vis_d)}, "
                f"match_max={match_max:.4f}")

            paint_bunker(obj)
            painted += 1

        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        return {"FINISHED"}


# ── Bunker panel ──────────────────────────────────────────────────────────────

class CVR_PT_bunker(bpy.types.Panel):
    bl_label       = "Bunkers"
    bl_idname      = "CVR_PT_bunker"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Course Vertex Painter"

    def draw(self, context):
        layout = self.layout
        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvr.paint_bunker", icon="NODE_MATERIAL")
        layout.separator()
        layout.label(text="Paint rough first — bunker", icon="INFO")
        layout.label(text="derives color from neighbors.")


# ══════════════════════════════════════════════════════════════════════════════
# DEEP ROUGH
# Base color: Red perimeter, fading inward through Green then Black.
# R channel = edge / boundary (sprinkler reach, adjacent to fairway/semi)
# G channel = lush dense grass just inside the edge
# Black (all zero) = dense uncut interior
#
# Band structure from boundary inward:
#   0 m          → pure Red
#   0 → green_d  → smoothstep Red → Green  (green_d varies 0.5–1.0 m per position)
#   green_d      → pure Green
#   green_d → black_d → smoothstep Green → Black  (gap varies 0.5–1.0 m per position)
#   black_d +    → pure Black (to center)
# ══════════════════════════════════════════════════════════════════════════════

def _build_boundary_distances(mesh, obj):
    """Per-vertex world-space distance to the nearest open boundary edge vertex.
    Returns a float list aligned with mesh.vertices."""
    mw = obj.matrix_world

    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_vis = {v.index for e in bm.edges if e.is_boundary for v in e.verts}
    bm.free()

    if not boundary_vis:
        # Closed mesh — treat everything as deep interior
        return [999.0] * len(mesh.vertices)

    b_positions = [mw @ mesh.vertices[vi].co for vi in boundary_vis]
    kd = KDTree(len(b_positions))
    for i, pos in enumerate(b_positions):
        kd.insert(pos, i)
    kd.balance()

    return [kd.find(mw @ v.co)[2] for v in mesh.vertices]


def paint_deep_rough(obj, seed, band_scale=1.0):
    mesh = obj.data
    if seed >= 0:
        random.seed(seed)

    color_data, color_attr = _ensure_color_layer(mesh)
    loop_map  = _build_loop_map(mesh, obj)
    vert_dist = _build_boundary_distances(mesh, obj)

    # Flood-fill red as the starting point
    for li, _, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    # Spatial scale of band-width variation — patches ~5 m across
    NOISE_SCALE = 5.0

    for li, wco, vi in loop_map:
        d = vert_dist[vi]

        # Two independent noise values to vary the two band widths.
        # The large coordinate offset on n2 decorrelates it from n1.
        n1 = _smooth_noise_2d(wco, NOISE_SCALE)
        n2 = _smooth_noise_2d(Vector((wco.x + 173.3, wco.y + 89.7, wco.z)), NOISE_SCALE)

        # green_dist: how far from edge until green peaks (0.5–1.0 m base, scaled)
        # black_dist: how far from edge until black peaks (green_dist + 0.5–1.0 m base, scaled)
        green_dist = (0.5 + n1 * 0.5) * band_scale
        black_dist = green_dist + (0.5 + n2 * 0.5) * band_scale

        if d <= green_dist:
            # Red → Green
            t  = d / green_dist
            ts = t * t * (3.0 - 2.0 * t)          # smoothstep
            r, g, b = 1.0 - ts, ts, 0.0
        elif d <= black_dist:
            # Green → Black
            t  = (d - green_dist) / (black_dist - green_dist)
            ts = t * t * (3.0 - 2.0 * t)
            r, g, b = 0.0, 1.0 - ts, 0.0
        else:
            # Pure black interior
            r, g, b = 0.0, 0.0, 0.0

        setattr(color_data[li], color_attr, (r, g, b, 1.0))

    mesh.update()


# ── Deep Rough operator ───────────────────────────────────────────────────────

class CVR_OT_paint_deep_rough(bpy.types.Operator):
    bl_idname      = "cvr.paint_deep_rough"
    bl_label       = "Deep Rough"
    bl_description = ("Vertex paint selected meshes as deep rough — red perimeter "
                      "fading to green then black toward the interior")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvr_props
        seed    = props.seed if props.use_seed else -1
        painted = 0
        for obj in context.selected_objects:
            if obj.type == "MESH":
                paint_deep_rough(obj, seed, band_scale=props.deep_rough_band_scale)
                painted += 1
        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Deep Rough paint applied to {painted} object(s).")
        return {"FINISHED"}


# ── Deep Rough panel ──────────────────────────────────────────────────────────

class CVR_PT_deep_rough(bpy.types.Panel):
    bl_label       = "Deep Rough"
    bl_idname      = "CVR_PT_deep_rough"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Course Vertex Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.cvr_props

        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvr.paint_deep_rough", icon="NODE_MATERIAL")

        layout.separator()
        box = layout.box()
        box.label(text="Band Depth", icon="ARROW_LEFTRIGHT")
        box.prop(props, "deep_rough_band_scale", text="Scale")

        layout.separator()
        box = layout.box()
        box.label(text="Options", icon="SETTINGS")
        row = box.row()
        row.prop(props, "use_seed", text="Fixed Seed")
        sub = row.row()
        sub.enabled = props.use_seed
        sub.prop(props, "seed", text="")


# ══════════════════════════════════════════════════════════════════════════════
# CART PATHS
# ══════════════════════════════════════════════════════════════════════════════
# Base color: Red (R=1, G=0, B=0) — concrete surface
# G channel  = grass / weeds creeping inward from the boundary edges
#              penetration depth varies 0–0.8 m per location via noise
# ══════════════════════════════════════════════════════════════════════════════

_CARTPATH_MAX_DEPTH   = 0.8   # metres — hard ceiling on grass penetration
_CARTPATH_NOISE_SCALE = 2.5   # metres — spatial period of depth variation
_CARTPATH_GREEN_PEAK  = 0.85  # green channel value right at the edge


def _add_cart_path_crossings(loop_map, mesh, obj, color_data, color_attr, vert_dist):
    """Paint full-width grass crossings at ~2 random locations per 50 m of path."""
    mw = obj.matrix_world

    # Rough path length: longest XY extent of the bounding box.
    corners  = [mw @ Vector(c) for c in obj.bound_box]
    x_range  = max(c.x for c in corners) - min(c.x for c in corners)
    y_range  = max(c.y for c in corners) - min(c.y for c in corners)
    path_len = max(x_range, y_range)

    # Crossing radius = path half-width + small margin so green reaches both edges.
    half_width = max(vert_dist) if vert_dist else 2.0
    cross_r    = half_width + 0.4

    count   = max(1, round(path_len / 50.0) * 2)
    min_sep = max(cross_r * 3.0, path_len / (count + 1))

    polys   = list(mesh.polygons)
    centres = []
    for _ in range(count * 30):
        if len(centres) >= count:
            break
        poly   = random.choice(polys)
        centre = mw @ poly.center
        if all((centre - c).length > min_sep for c in centres):
            centres.append(centre)

    for centre in centres:
        for li, wco, _ in loop_map:
            d = (wco - centre).length
            if d >= cross_r:
                continue
            t      = d / cross_r
            weight = (1.0 - t * t * (3.0 - 2.0 * t)) * _CARTPATH_GREEN_PEAK
            cur    = getattr(color_data[li], color_attr)
            r, g, b, a = cur[0], cur[1], cur[2], cur[3]
            setattr(color_data[li], color_attr,
                    (max(0.0, r - weight * 0.5), max(g, weight), b, a))


def paint_cart_path(obj, seed):
    mesh = obj.data
    if seed >= 0:
        random.seed(seed)

    color_data, color_attr = _ensure_color_layer(mesh)
    loop_map  = _build_loop_map(mesh, obj)
    vert_dist = _build_boundary_distances(mesh, obj)

    # Flood-fill base red
    for li, _, _ in loop_map:
        setattr(color_data[li], color_attr, (1.0, 0.0, 0.0, 1.0))

    for li, wco, vi in loop_map:
        d = vert_dist[vi]
        if d >= _CARTPATH_MAX_DEPTH:
            continue

        # Noise-varied local penetration depth (0.0 – MAX_DEPTH).
        # When noise is low the grass barely reaches this spot; when high it
        # creeps in up to 0.8 m.
        n = _smooth_noise_2d(wco, _CARTPATH_NOISE_SCALE)
        local_depth = n * _CARTPATH_MAX_DEPTH
        if local_depth < 0.02 or d >= local_depth:
            continue

        # Smoothstep weight: 1 at the edge, 0 at local_depth
        t      = d / local_depth
        ts     = t * t * (3.0 - 2.0 * t)
        weight = (1.0 - ts) * _CARTPATH_GREEN_PEAK

        r = max(0.0, 1.0 - weight * 0.5)
        g = weight
        setattr(color_data[li], color_attr, (r, g, 0.0, 1.0))

    _add_cart_path_crossings(loop_map, mesh, obj, color_data, color_attr, vert_dist)
    mesh.update()


# ── Cart Path operator ────────────────────────────────────────────────────────

class CVR_OT_paint_cart_path(bpy.types.Operator):
    bl_idname      = "cvr.paint_cart_path"
    bl_label       = "Cart Path"
    bl_description = ("Vertex paint selected meshes as cart paths — "
                      "red base with randomised green grass creeping in "
                      "from the edges (0–0.8 m depth)")
    bl_options     = {"REGISTER", "UNDO"}

    def execute(self, context):
        props   = context.scene.cvr_props
        seed    = props.seed if props.use_seed else -1
        painted = 0
        for obj in context.selected_objects:
            if obj.type == "MESH":
                paint_cart_path(obj, seed)
                painted += 1
        if painted == 0:
            self.report({"WARNING"}, "No mesh objects selected.")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Cart Path paint applied to {painted} object(s).")
        return {"FINISHED"}


# ── Cart Path panel ───────────────────────────────────────────────────────────

class CVR_PT_cart_path(bpy.types.Panel):
    bl_label       = "Cart Path"
    bl_idname      = "CVR_PT_cart_path"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Course Vertex Painter"

    def draw(self, context):
        layout = self.layout
        props  = context.scene.cvr_props

        layout.label(text="Apply to selected mesh(es):")
        col = layout.column(align=True)
        col.scale_y = 1.4
        col.operator("cvr.paint_cart_path", icon="NODE_MATERIAL")

        layout.separator()
        box = layout.box()
        box.label(text="Options", icon="SETTINGS")
        row = box.row()
        row.prop(props, "use_seed", text="Fixed Seed")
        sub = row.row()
        sub.enabled = props.use_seed
        sub.prop(props, "seed", text="")


# ══════════════════════════════════════════════════════════════════════════════
# GREENS
# ══════════════════════════════════════════════════════════════════════════════

# recipes, operators, and panel go here


# ══════════════════════════════════════════════════════════════════════════════
# SHARED SCENE PROPERTIES
# ══════════════════════════════════════════════════════════════════════════════

class CVRProps(bpy.types.PropertyGroup):
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
    fairway_height_blue: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Height Gradient",
        description = "How strongly elevation drives the dry (blue) tint on fairways",
        default     = 0.03,
        min         = 0.0,
        max         = 0.20,
        step        = 1,
        precision   = 3,
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
    fairway_use_height_blue: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Height Gradient (Dry High Ground)",
        description = ("Adds a subtle blue (dry) tint to vertices on higher terrain. "
                       "Low areas stay pure red; ridges and rises get a bleached look. "
                       "Strength is set by the slider below."),
        default     = True,
    )
    fairway_use_patch_blue: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Organic Dry Patches",
        description = ("Scatters 2 organic elliptical dry patches randomly across the "
                       "fairway surface. Each is randomly oriented and stretched for a "
                       "natural look. Strength is set by the slider below."),
        default     = True,
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
    rough_type: bpy.props.EnumProperty(  # type: ignore[assignment]
        name        = "Rough Type",
        description = "Painting recipe for rough surfaces",
        items       = [
            ("TYPE1", "Standard",
             "Height-driven dry patches with wet/dry proximity effects"),
            ("TYPE2", "Mow Lines",
             "Stripes parallel to the fairway edge, fading outward"),
        ],
        default     = "TYPE1",
    )
    rough_mow_depth: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Mow Depth",
        description = "How far from the fairway edge mow stripes extend (metres)",
        default     = 15.0,
        min         = 3.0,
        max         = 30.0,
        step        = 100,
        precision   = 1,
    )
    rough_mow_period: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Stripe Period",
        description = "Width of one full light+dark stripe cycle (metres)",
        default     = 2.5,
        min         = 1.0,
        max         = 8.0,
        step        = 10,
        precision   = 2,
    )
    rough_mow_strength: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Dry Zone Blue",
        description = "Blue channel strength applied uniformly beyond the mow depth (the dry outer zone)",
        default     = 0.45,
        min         = 0.0,
        max         = 0.70,
        step        = 1,
        precision   = 3,
    )
    rough_stripe_strength: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Stripe Intensity",
        description = "Peak blue on the light mow stripes near the fairway — keep subtle (0.05–0.15)",
        default     = 0.10,
        min         = 0.0,
        max         = 0.40,
        step        = 1,
        precision   = 3,
    )
    rough_lush_patch_count: bpy.props.IntProperty(  # type: ignore[assignment]
        name        = "Green Patches",
        description = "Number of organic lush (green) patches scattered in the dry zone",
        default     = 10,
        min         = 0,
        max         = 40,
    )
    rough_lush_patch_zone: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Patch Zone (m)",
        description = "How far beyond the mow depth patch centres can spawn — limits patches to near-fairway playing area",
        default     = 25.0,
        min         = 5.0,
        max         = 100.0,
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
    fairway_apron_black: bpy.props.BoolProperty(  # type: ignore[assignment]
        name        = "Paint Black on Apron (For Crossed Mowlines)",
        description = ("Darkens vertices to black within the apron radius of any "
                       "'Green - Blend' object. Creates a crossed mow-line look on the "
                       "apron zone around the putting surface. Radius set below."),
        default     = False,
    )
    rough_tee_seam_radius: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Tee Red Band",
        description = ("Metres of rough painted back to pure red around any Tee-Blend "
                       "mesh. 0 = disabled. 5 m is a good starting value; increase if "
                       "there is a visible colour break between the tee and rough."),
        default     = 0.3,
        min         = 0.0,
        max         = 20.0,
        step        = 10,
        precision   = 2,
    )
    deep_rough_band_scale: bpy.props.FloatProperty(  # type: ignore[assignment]
        name        = "Band Scale",
        description = ("Scales how deep the red→green and green→black colour bands "
                       "reach from the mesh edge. 1.0 = default (1–2 m total). "
                       "2.0 = 2–4 m. 4.0 = 4–8 m."),
        default     = 1.0,
        min         = 0.5,
        max         = 6.0,
        step        = 10,
        precision   = 1,
    )


# ══════════════════════════════════════════════════════════════════════════════
# REGISTRATION
# Add new operator and panel classes to _classes as each section is built out.
# ══════════════════════════════════════════════════════════════════════════════

_classes = (
    CVRProps,
    # Tee boxes
    CVR_OT_paint_tee_par3,
    CVR_OT_paint_tee_par45,
    CVR_PT_tee,
    # Fairways
    CVR_OT_paint_fairway,
    CVR_PT_fairway,
    # Rough
    CVR_OT_paint_rough,
    CVR_PT_rough,
    # Bunkers
    CVR_OT_paint_bunker,
    CVR_PT_bunker,
    # Deep Rough
    CVR_OT_paint_deep_rough,
    CVR_PT_deep_rough,
    # Cart Paths
    CVR_OT_paint_cart_path,
    CVR_PT_cart_path,
    # Greens    — add classes here
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.cvr_props = bpy.props.PointerProperty(type=CVRProps)


def unregister():
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
    del bpy.types.Scene.cvr_props


if __name__ == "__main__":
    register()
