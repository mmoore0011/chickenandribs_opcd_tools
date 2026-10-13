"""
Mesh Gap Detector  —  v0.6.0
============================
For every Spline mesh, finds boundary vertices (v.is_boundary) that don't
have a matching vertex from any other Spline mesh within the match tolerance.

Exceptions (not scanned):
- Objects whose name doesn't start with "Spline"
- Objects with the WaterPlane material

Installation
------------
Edit -> Preferences -> Add-ons -> Install -> pick this file -> Enable.
"""

bl_info = {
    "name": "Gap Tools",
    "author": "@chickenandribs",
    "version": (0, 6, 0),
    "blender": (3, 6, 0),
    "location": "Object Mode -> N-panel -> Mesh Tools",
    "description": "Detect boundary vertices not connected to any adjacent Spline mesh.",
    "category": "Mesh",
}

import bpy
import bmesh
import math
import mathutils


# ─────────────────────────────────────────────────────────────────────────────
# Constants / storage
# ─────────────────────────────────────────────────────────────────────────────

WATER_MATERIAL = "WaterPlane"
_gap_results   = []   # [(obj_name, [vert_indices])]


# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

def _has_water_material(obj):
    return any(
        slot.material and slot.material.name == WATER_MATERIAL
        for slot in obj.material_slots
    )


def _is_spline(obj, prefix):
    return not prefix or obj.name.startswith(prefix)


def _build_terrain_boundary_kd():
    """
    Build a 2D (XY only) KD-tree from the boundary vertices of the Terrain object.
    Z is zeroed out so elevation differences don't affect matching.
    Returns (kd, offset) or (None, None) if Terrain not found.
    """
    terrain = bpy.data.objects.get("Terrain")
    if terrain is None or terrain.type != "MESH":
        return None, None

    mat = terrain.matrix_world
    bm  = bmesh.new()
    bm.from_mesh(terrain.data)
    bm.verts.ensure_lookup_table()

    boundary_cos = []
    for v in bm.verts:
        if v.is_boundary:
            wco = mat @ v.co
            # Zero out Z — boundary matching is purely in XY
            boundary_cos.append(mathutils.Vector((wco.x, wco.y, 0.0)))
    bm.free()

    if not boundary_cos:
        return None, None

    offset = boundary_cos[0].copy()
    kd     = mathutils.kdtree.KDTree(len(boundary_cos))
    for idx, co in enumerate(boundary_cos):
        kd.insert(co - offset, idx)
    kd.balance()

    print(f"Terrain boundary KD-tree built: {len(boundary_cos)} verts.")
    return kd, offset


def _filter_terrain_boundary(vi_set, obj, terrain_kd, terrain_offset, margin):
    """
    Remove verts that are within margin of the Terrain boundary in XY.
    Z is ignored — the project boundary is defined horizontally.
    """
    if terrain_kd is None or not vi_set:
        return vi_set

    mat      = obj.matrix_world
    filtered = set()

    for vi in vi_set:
        wco      = mat @ obj.data.vertices[vi].co
        # Zero out Z for the query too
        query_co = mathutils.Vector((wco.x, wco.y, 0.0))
        _co, _idx, dist = terrain_kd.find(query_co - terrain_offset)
        if dist > margin:
            filtered.add(vi)

    return filtered


def _get_boundary_verts(obj):
    """
    Return list of (world_co, vert_index) for every boundary vertex
    (v.is_boundary) using BMesh — simple and reliable.
    """
    mat = obj.matrix_world
    bm  = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()

    result = []
    for v in bm.verts:
        if v.is_boundary:
            result.append((mat @ v.co, v.index))

    bm.free()
    return result


# ─────────────────────────────────────────────────────────────────────────────
# Core detection
# ─────────────────────────────────────────────────────────────────────────────

def _run_detection(operator, context, selected_only=False):
    global _gap_results
    _gap_results = []

    threshold  = context.scene.mgd_match_precision
    prefix     = operator.name_prefix
    skip_water = operator.skip_water

    # All visible Spline meshes (used for both scanning and lookup)
    all_spline = [
        o for o in context.view_layer.objects
        if o.type == "MESH"
        and not o.hide_viewport
        and _is_spline(o, prefix)
        and not (skip_water and _has_water_material(o))
    ]

    if not all_spline:
        operator.report({"WARNING"}, "No matching visible mesh objects found.")
        return {"CANCELLED"}

    # Candidates — what we actually report gaps on
    if selected_only:
        active = context.active_object
        if active is None or active.type != "MESH":
            operator.report({"ERROR"}, "No active mesh object selected.")
            return {"CANCELLED"}
        candidates = [active]
    else:
        candidates = all_spline

    # Build KD-tree of ALL boundary verts from ALL Spline meshes.
    # Subtract offset to maintain float precision at large coordinates.
    print("Building KD-tree from all Spline boundary verts...")
    all_lookup = []   # (world_co, obj_name)
    for obj in all_spline:
        for world_co, vi in _get_boundary_verts(obj):
            all_lookup.append((world_co, obj.name))

    if not all_lookup:
        operator.report({"INFO"}, "No boundary edges found in any mesh.")
        return {"FINISHED"}

    offset = all_lookup[0][0].copy()
    kd     = mathutils.kdtree.KDTree(len(all_lookup))
    for idx, (world_co, obj_name) in enumerate(all_lookup):
        kd.insert(world_co - offset, idx)
    kd.balance()

    print(f"KD-tree built with {len(all_lookup)} boundary verts.")

    # For each candidate, check every boundary vert
    gap_vi_by_obj = {}

    for obj in candidates:
        bverts = _get_boundary_verts(obj)
        if not bverts:
            continue

        for world_co, vi in bverts:
            # Find any neighbour within threshold from a DIFFERENT object
            neighbours = kd.find_range(world_co - offset, threshold)
            connected  = any(
                all_lookup[nidx][1] != obj.name
                for _, nidx, _ in neighbours
            )
            if not connected:
                gap_vi_by_obj.setdefault(obj.name, set()).add(vi)

    # Build Terrain boundary KD-tree once for perimeter filtering
    terrain_kd, terrain_offset = _build_terrain_boundary_kd()
    terrain_margin = operator.terrain_margin
    if terrain_kd is None:
        print("Note: 'Terrain' object not found — perimeter filtering disabled.")

    # Store results
    total         = 0
    skipped_perim = 0
    for obj_name, vi_set in gap_vi_by_obj.items():
        obj      = bpy.data.objects.get(obj_name)
        filtered = _filter_terrain_boundary(vi_set, obj, terrain_kd, terrain_offset, terrain_margin)
        skipped_perim += len(vi_set) - len(filtered)

        if not filtered:
            continue

        _gap_results.append((obj_name, sorted(filtered)))
        total += len(filtered)

    perim_note = f" ({skipped_perim} perimeter object(s) skipped.)" if skipped_perim else ""

    if not _gap_results:
        operator.report({"INFO"}, f"No gaps detected.{perim_note}")
    else:
        operator.report(
            {"WARNING"},
            f"{total} gap vert(s) across {len(_gap_results)} object(s).{perim_note} "
            "See the Gap Detector panel."
        )
        print("\n=== GAP DETECTOR RESULTS ===")
        for obj_name, vis in _gap_results:
            preview = str(vis[:10]) + ("…" if len(vis) > 10 else "")
            print(f"  {obj_name}: {len(vis)} gap vert(s)  indices: {preview}")
        print("============================\n")

    return {"FINISHED"}


# ─────────────────────────────────────────────────────────────────────────────
# Shared operator properties
# ─────────────────────────────────────────────────────────────────────────────

class _GapDetectorProps:
    skip_water: bpy.props.BoolProperty(
        name='Skip "WaterPlane" meshes',
        description='Ignore meshes with the WaterPlane material',
        default=True,
    )
    name_prefix: bpy.props.StringProperty(
        name="Object Name Prefix",
        description='Only scan objects starting with this prefix. Use "Spline" to exclude helper objects.',
        default="Spline",
    )
    terrain_margin: bpy.props.FloatProperty(
        name="Terrain Margin",
        description=(
            "Boundary verts within this XY distance of the Terrain object's boundary "
            "are treated as project perimeter and ignored."
        ),
        default=6.0,
        min=0.0,
        soft_max=50.0,
        unit="LENGTH",
    )


# ─────────────────────────────────────────────────────────────────────────────
# Operators
# ─────────────────────────────────────────────────────────────────────────────

class MESH_OT_detect_gaps(_GapDetectorProps, bpy.types.Operator):
    bl_idname      = "mesh.detect_gaps"
    bl_label       = "Detect Mesh Gaps in Entire Project (Object Mode)"
    bl_description = "Scan all visible Spline meshes for unconnected boundary vertices"
    bl_options     = {"REGISTER"}

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT"

    def execute(self, context):
        return _run_detection(self, context, selected_only=False)

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False
        layout.prop(self, "name_prefix")
        layout.prop(self, "skip_water")
        layout.prop(self, "terrain_margin")


class MESH_OT_detect_gaps_selected(_GapDetectorProps, bpy.types.Operator):
    bl_idname      = "mesh.detect_gaps_selected"
    bl_label       = "Detect Mesh Gaps in Selected Mesh (Object Mode)"
    bl_description = "Scan only the active selected mesh for unconnected boundary vertices"
    bl_options     = {"REGISTER"}

    @classmethod
    def poll(cls, context):
        return (
            context.mode == "OBJECT"
            and context.active_object is not None
            and context.active_object.type == "MESH"
        )

    def execute(self, context):
        return _run_detection(self, context, selected_only=True)


# ─────────────────────────────────────────────────────────────────────────────
# Select + Zoom Operator
# ─────────────────────────────────────────────────────────────────────────────

class MESH_OT_select_gap_object(bpy.types.Operator):
    """Enter Edit Mode, select gap vertices, and zoom to them."""
    bl_idname  = "mesh.select_gap_object"
    bl_label   = "Inspect Gap"
    bl_options = {"REGISTER", "UNDO"}

    obj_name: bpy.props.StringProperty()

    @classmethod
    def poll(cls, context):
        return context.mode in {"OBJECT", "EDIT_MESH"}

    def execute(self, context):
        obj = bpy.data.objects.get(self.obj_name)
        if obj is None:
            self.report({"ERROR"}, f"Object '{self.obj_name}' not found.")
            return {"CANCELLED"}

        vi_list = next((vis for name, vis in _gap_results if name == self.obj_name), None)
        if vi_list is None:
            self.report({"ERROR"}, "No gap data — please re-run detection.")
            return {"CANCELLED"}

        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")

        bm = bmesh.from_edit_mesh(obj.data)
        bm.verts.ensure_lookup_table()

        for v in bm.verts:
            v.select = False
        bm.select_flush(False)

        vi_set = set(vi_list)
        for v in bm.verts:
            if v.index in vi_set:
                v.select = True

        bm.select_flush(True)
        bmesh.update_edit_mesh(obj.data)
        bpy.ops.view3d.view_selected()

        self.report({"INFO"}, f"Selected {len(vi_list)} gap vert(s) in '{self.obj_name}'.")
        return {"FINISHED"}


class MESH_OT_snap_gap_verts(bpy.types.Operator):
    """Snap the flagged gap vertices to the nearest vertex in any other Spline mesh."""
    bl_idname  = "mesh.snap_gap_verts"
    bl_label   = "Snap Gap Verts"
    bl_options = {"REGISTER", "UNDO"}

    obj_name: bpy.props.StringProperty()

    SNAP_RADIUS = 1.0   # metres — if nothing found within this, error out

    @classmethod
    def poll(cls, context):
        return context.mode in {"OBJECT", "EDIT_MESH"} and bool(_gap_results)

    def execute(self, context):
        obj = bpy.data.objects.get(self.obj_name)
        if obj is None:
            self.report({"ERROR"}, f"Object '{self.obj_name}' not found.")
            return {"CANCELLED"}

        vi_list = next((vis for name, vis in _gap_results if name == self.obj_name), None)
        if vi_list is None:
            self.report({"ERROR"}, "No gap data — please re-run detection.")
            return {"CANCELLED"}

        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        src_mat     = obj.matrix_world
        src_mat_inv = src_mat.inverted()
        prefix      = context.scene.mgd_name_prefix

        # Build a KD-tree of ALL verts from all other Spline objects
        all_verts = []   # (world_co, obj_name)
        for other in context.view_layer.objects:
            if other == obj or other.type != "MESH" or other.hide_viewport:
                continue
            if prefix and not other.name.startswith(prefix):
                continue
            other_mat = other.matrix_world
            for v in other.data.vertices:
                all_verts.append(other_mat @ v.co)

        if not all_verts:
            self.report({"ERROR"}, "No nearby mesh found to snap to.")
            return {"CANCELLED"}

        # Subtract offset for precision at large coordinates
        offset = all_verts[0].copy()
        kd     = mathutils.kdtree.KDTree(len(all_verts))
        for idx, wco in enumerate(all_verts):
            kd.insert(wco - offset, idx)
        kd.balance()

        # Enter Edit Mode and snap gap verts
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")

        bm = bmesh.from_edit_mesh(obj.data)
        bm.verts.ensure_lookup_table()

        vi_set = set(vi_list)
        moved  = 0
        no_target = 0

        for v in bm.verts:
            if v.index not in vi_set:
                continue
            world_co = src_mat @ v.co
            result   = kd.find(world_co - offset)
            if result is None:
                no_target += 1
                continue
            _co, idx, dist = result
            if dist > self.SNAP_RADIUS:
                no_target += 1
                continue
            v.co = src_mat_inv @ all_verts[idx]
            moved += 1

        bmesh.update_edit_mesh(obj.data, loop_triangles=True)
        bpy.ops.object.mode_set(mode="OBJECT")

        if no_target > 0 and moved == 0:
            self.report({"ERROR"}, "No nearby mesh found to snap to.")
            return {"CANCELLED"}

        parts = [f"Snapped {moved} vert(s) in '{self.obj_name}'."]
        if no_target:
            parts.append(f"{no_target} vert(s) had no mesh within {self.SNAP_RADIUS}m.")
        self.report({"INFO"}, " ".join(parts))
        return {"FINISHED"}



class MESH_OT_debug_gap_vert(bpy.types.Operator):
    """Print debug info for the first flagged vert to the System Console."""
    bl_idname  = "mesh.debug_gap_vert"
    bl_label   = "Debug Gap Vert"
    bl_options = {"REGISTER"}

    obj_name: bpy.props.StringProperty()

    @classmethod
    def poll(cls, context):
        return context.mode in {"OBJECT", "EDIT_MESH"} and bool(_gap_results)

    def execute(self, context):
        vi_list = next((vis for name, vis in _gap_results if name == self.obj_name), None)
        if vi_list is None:
            self.report({"ERROR"}, f"No gap data for '{self.obj_name}'.")
            return {"CANCELLED"}

        obj = bpy.data.objects.get(self.obj_name)
        if obj is None:
            self.report({"ERROR"}, f"Object '{self.obj_name}' not found.")
            return {"CANCELLED"}

        vi       = vi_list[0]
        mat      = obj.matrix_world
        local_co = obj.data.vertices[vi].co
        world_co = mat @ local_co
        threshold = context.scene.mgd_match_precision

        print("\n========== GAP DETECTOR DEBUG ==========")
        print(f"Object       : {self.obj_name}")
        print(f"Vertex index : {vi}")
        print(f"World co     : ({world_co.x:.6f}, {world_co.y:.6f}, {world_co.z:.6f})")
        print(f"Threshold    : {threshold}m")
        print()
        print("--- Nearest boundary verts from other Spline objects (within 2m) ---")

        closest = []
        prefix  = context.scene.mgd_name_prefix
        for other in context.view_layer.objects:
            if other == obj or other.type != "MESH" or other.hide_viewport:
                continue
            if prefix and not other.name.startswith(prefix):
                continue
            other_mat = other.matrix_world
            bm2 = bmesh.new()
            bm2.from_mesh(other.data)
            bm2.verts.ensure_lookup_table()
            for v in bm2.verts:
                if not v.is_boundary:
                    continue
                owco = other_mat @ v.co
                dist = (owco - world_co).length
                if dist < 2.0:
                    closest.append((dist, other.name, v.index, owco))
            bm2.free()

        closest.sort(key=lambda x: x[0])
        if not closest:
            print("  No boundary verts from other Spline objects within 2m!")
        else:
            for dist, oname, ovi, owco in closest[:10]:
                flag = "<<< WITHIN THRESHOLD" if dist <= threshold else ""
                print(f"  {oname} vi={ovi}  dist={dist:.8f}m  {flag}")
                print(f"    world=({owco.x:.6f}, {owco.y:.6f}, {owco.z:.6f})")

        print("=========================================\n")
        self.report({"INFO"}, "Debug info printed to System Console.")
        return {"FINISHED"}


class MESH_OT_fix_selected_gap_verts(bpy.types.Operator):
    """Check selected boundary verts for gaps and snap only the unconnected ones."""
    bl_idname  = "mesh.fix_selected_gap_verts"
    bl_label   = "Fix Gaps in Selected Vert(s) (Edit Mode)"
    bl_options = {"REGISTER", "UNDO"}

    SNAP_RADIUS = 1.0   # metres

    @classmethod
    def poll(cls, context):
        return (
            context.mode == "EDIT_MESH"
            and context.active_object is not None
            and context.active_object.type == "MESH"
        )

    def execute(self, context):
        obj       = context.active_object
        threshold = context.scene.mgd_match_precision
        prefix    = context.scene.mgd_name_prefix
        src_mat     = obj.matrix_world
        src_mat_inv = src_mat.inverted()

        bm = bmesh.from_edit_mesh(obj.data)
        bm.verts.ensure_lookup_table()

        selected_boundary = [v for v in bm.verts if v.select and v.is_boundary]

        if not selected_boundary:
            self.report({"WARNING"}, "No selected boundary vertices found. Select verts in Edit Mode first.")
            return {"CANCELLED"}

        # Collect boundary verts and all verts from every other Spline mesh
        other_boundary_cos = []   # for gap check
        other_all_cos      = []   # for snapping

        for other in context.view_layer.objects:
            if other == obj or other.type != "MESH" or other.hide_viewport:
                continue
            if prefix and not other.name.startswith(prefix):
                continue
            other_mat = other.matrix_world
            for v in other.data.vertices:
                other_all_cos.append(other_mat @ v.co)
            bm2 = bmesh.new()
            bm2.from_mesh(other.data)
            bm2.verts.ensure_lookup_table()
            for v in bm2.verts:
                if v.is_boundary:
                    other_boundary_cos.append(other_mat @ v.co)
            bm2.free()

        if not other_boundary_cos or not other_all_cos:
            self.report({"ERROR"}, "No other Spline meshes found.")
            return {"CANCELLED"}

        # KD-tree for gap check (boundary verts only, precision offset)
        off_b    = other_boundary_cos[0].copy()
        kd_check = mathutils.kdtree.KDTree(len(other_boundary_cos))
        for idx, co in enumerate(other_boundary_cos):
            kd_check.insert(co - off_b, idx)
        kd_check.balance()

        # KD-tree for snapping (all verts)
        off_s   = other_all_cos[0].copy()
        kd_snap = mathutils.kdtree.KDTree(len(other_all_cos))
        for idx, co in enumerate(other_all_cos):
            kd_snap.insert(co - off_s, idx)
        kd_snap.balance()

        moved            = 0
        skipped_fine     = 0
        skipped_no_target = 0

        for v in selected_boundary:
            world_co = src_mat @ v.co

            # Already connected — leave it alone
            if kd_check.find_range(world_co - off_b, threshold):
                skipped_fine += 1
                continue

            # Gap vert — snap to nearest vert in any other Spline mesh
            result = kd_snap.find(world_co - off_s)
            if result is None:
                skipped_no_target += 1
                continue
            _, idx, dist = result
            if dist > self.SNAP_RADIUS:
                skipped_no_target += 1
                continue

            v.co = src_mat_inv @ other_all_cos[idx]
            moved += 1

        bmesh.update_edit_mesh(obj.data, loop_triangles=True)

        if moved == 0 and skipped_fine == 0:
            self.report({"ERROR"}, f"No gap verts snapped — nothing within {self.SNAP_RADIUS}m.")
            return {"CANCELLED"}

        parts = [f"Fixed {moved} gap vert(s)."]
        if skipped_fine:
            parts.append(f"{skipped_fine} already connected (skipped).")
        if skipped_no_target:
            parts.append(f"{skipped_no_target} had no target within {self.SNAP_RADIUS}m.")
        self.report({"INFO"}, " ".join(parts))
        return {"FINISHED"}


# ─────────────────────────────────────────────────────────────────────────────
# N-Panels
# ─────────────────────────────────────────────────────────────────────────────

def _draw_gap_panel(layout, context):
    sc = context.scene
    box = layout.box()
    box.label(text="PLEASE MAKE A BACKUP OF YOUR", icon="ERROR")
    box.label(text="PROJECT BEFORE USING THIS TOOL")
    layout.separator()
    layout.prop(sc, "mgd_match_precision")
    layout.separator()
    layout.operator(MESH_OT_detect_gaps.bl_idname, icon="ZOOM_ALL")
    layout.operator(MESH_OT_detect_gaps_selected.bl_idname, icon="OBJECT_DATA")
    layout.operator(MESH_OT_fix_selected_gap_verts.bl_idname, icon="SNAP_ON")
    layout.separator()

    if not _gap_results:
        layout.label(text="No results yet — run detection.", icon="INFO")
        return

    total = sum(len(vis) for _, vis in _gap_results)
    box   = layout.box()
    box.label(text=f"{total} gap vert(s) across {len(_gap_results)} object(s).", icon="ERROR")

    col = layout.column(align=True)
    for obj_name, vis in _gap_results:
        row = col.row(align=True)
        row.label(text=f"{obj_name}  ({len(vis)} vert(s))", icon="MESH_DATA")
        op          = row.operator(MESH_OT_select_gap_object.bl_idname, text="", icon="ZOOM_SELECTED")
        op.obj_name = obj_name
        snap        = row.operator(MESH_OT_snap_gap_verts.bl_idname, text="", icon="SNAP_ON")
        snap.obj_name = obj_name
        dbg         = row.operator(MESH_OT_debug_gap_vert.bl_idname, text="", icon="CONSOLE")
        dbg.obj_name = obj_name


class VIEW3D_PT_gap_detector_object(bpy.types.Panel):
    bl_label       = "Gap Detector v0.6.0"
    bl_idname      = "VIEW3D_PT_gap_detector_object"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Gap Tools"
    bl_context     = "objectmode"

    def draw(self, context):
        _draw_gap_panel(self.layout, context)


class VIEW3D_PT_gap_detector_edit(bpy.types.Panel):
    bl_label       = "Gap Detector v0.6.0"
    bl_idname      = "VIEW3D_PT_gap_detector_edit"
    bl_space_type  = "VIEW_3D"
    bl_region_type = "UI"
    bl_category    = "Gap Tools"
    bl_context     = "mesh_edit"

    def draw(self, context):
        _draw_gap_panel(self.layout, context)


# ─────────────────────────────────────────────────────────────────────────────
# Registration
# ─────────────────────────────────────────────────────────────────────────────

_classes = (
    MESH_OT_detect_gaps,
    MESH_OT_detect_gaps_selected,
    MESH_OT_fix_selected_gap_verts,
    MESH_OT_select_gap_object,
    MESH_OT_snap_gap_verts,
    MESH_OT_debug_gap_vert,
    VIEW3D_PT_gap_detector_object,
    VIEW3D_PT_gap_detector_edit,
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.mgd_match_precision = bpy.props.FloatProperty(
        name="Match Tolerance",
        description="Max distance between vertices to count as connected (meters)",
        default=0.001,
        min=0.0001,
        soft_max=0.1,
        precision=4,
        unit="LENGTH",
    )
    bpy.types.Scene.mgd_name_prefix = bpy.props.StringProperty(
        name="Name Prefix",
        default="Spline",
    )


def unregister():
    del bpy.types.Scene.mgd_match_precision
    del bpy.types.Scene.mgd_name_prefix
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
