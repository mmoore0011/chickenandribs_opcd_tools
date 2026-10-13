"""
murmuration_setup.py
──────────────────────────────────────────────────────────────────────────────
Blender Python script — open in Blender's Text Editor and click "Run Script".

Automates the starling murmuration boid-particle scene from Simon Obstall's
tutorial. Builds the emitter, sky, goal empty with sine/cosine drivers, boid
particle system, camera with track-to constraint, and key/fill lights.

BEFORE RUNNING
  1. Set SKY_TEXTURE_PATH  — path to a sky image (or leave blank for grey).
  2. Set STARLING_BLEND_PATH — path to Starling.blend (or leave blank; halos
     will be used so you can tune the simulation without the mesh asset).
  3. If Blender shows a warning about driver expressions, enable
     "Allow Execution of Embedded Scripts" in:
     Edit > Preferences > File Paths > Auto Run Python Scripts
──────────────────────────────────────────────────────────────────────────────
"""

import bpy
import math

# ── USER CONFIG ───────────────────────────────────────────────────────────────
SKY_TEXTURE_PATH    = r""   # e.g.  r"C:\Assets\sky.jpg"
STARLING_BLEND_PATH = r""   # e.g.  r"C:\Assets\Starling.blend"

WORKING_BIRD_COUNT  = 500   # Fast while tuning; raise to 2500 for final render
# ─────────────────────────────────────────────────────────────────────────────


def _set_active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


# ── 1. SCENE ──────────────────────────────────────────────────────────────────

def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end   = 750


# ── 2. EMITTER ────────────────────────────────────────────────────────────────

def create_emitter():
    """
    Cylinder (radius 2.3) rotated 90° on Y, then all faces deleted except
    the single cap — making a disc that emits particles from one flat face.
    Placed at X = -20 so it starts well off-camera.
    """
    bpy.ops.mesh.primitive_cylinder_add(
        radius=2.3, depth=2,
        enter_editmode=False,
        location=(-20, 0, 0),
    )
    obj = bpy.context.active_object
    obj.name = "emitter"

    # Apply rotation so local +X is the emission face direction
    obj.rotation_euler[1] = math.radians(90)
    bpy.ops.object.transform_apply(rotation=True)

    # Keep only the cap face (now the face furthest in +X after applying rotation)
    bpy.ops.object.editmode_toggle()
    bpy.ops.mesh.select_mode(type='FACE')
    bpy.ops.mesh.select_all(action='DESELECT')

    import bmesh
    bm = bmesh.from_edit_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    max(bm.faces, key=lambda f: f.calc_center_median().x).select = True
    bmesh.update_edit_mesh(obj.data)

    bpy.ops.mesh.select_all(action='INVERT')
    bpy.ops.mesh.delete(type='FACE')
    bpy.ops.object.editmode_toggle()

    return obj


# ── 3. SKY ────────────────────────────────────────────────────────────────────

def create_sky():
    """
    Large matte plane behind the scene.
    Tutorial sizes: add plane size=20, rotate 90 X, move Y=40, scale X×1.4,
    later scale X×3.5 and Z×3.5. Net result: width ≈ 98, height = 70 units.
    """
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 40, 0))
    sky = bpy.context.active_object
    sky.name = "Sky"

    sky.rotation_euler[0] = math.radians(90)
    bpy.ops.object.transform_apply(rotation=True)
    sky.scale = (98, 1, 70)
    bpy.ops.object.transform_apply(scale=True)

    mat = bpy.data.materials.new("Sky_Mat")
    mat.use_nodes = True
    sky.data.materials.append(mat)

    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    out  = nodes.new('ShaderNodeOutputMaterial')
    bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    out.location  = (300, 0)
    bsdf.location = (0, 0)
    links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])

    # Zero specular — handle renamed input between Blender 3.x and 4.x
    for spec_name in ('Specular IOR Level', 'Specular'):
        if spec_name in bsdf.inputs:
            bsdf.inputs[spec_name].default_value = 0.0
            break
    bsdf.inputs['Roughness'].default_value = 1.0

    if SKY_TEXTURE_PATH:
        tex = nodes.new('ShaderNodeTexImage')
        tex.location = (-300, 0)
        try:
            tex.image = bpy.data.images.load(SKY_TEXTURE_PATH, check_existing=True)
            links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
            print("[Murmuration] Sky texture loaded.")
        except RuntimeError as err:
            print(f"[Murmuration] Sky texture skipped ({err}) — using grey.")
            bsdf.inputs['Base Color'].default_value = (0.45, 0.55, 0.70, 1.0)
    else:
        bsdf.inputs['Base Color'].default_value = (0.45, 0.55, 0.70, 1.0)

    return sky


# ── 4. STARLING COLLECTION ───────────────────────────────────────────────────

def link_starling_collection():
    """
    Links 'Starling collection' from Starling.blend.
    The collection contains three starling meshes animating at different
    wing-flap speeds. Returns the collection or None if path not set.
    """
    if not STARLING_BLEND_PATH:
        print("[Murmuration] STARLING_BLEND_PATH not set — particle halos used.")
        return None

    try:
        with bpy.data.libraries.load(STARLING_BLEND_PATH, link=True) as (src, dst):
            if 'Starling collection' in src.collections:
                dst.collections = ['Starling collection']
            else:
                print("[Murmuration] 'Starling collection' not found in blend file.")
                return None
    except OSError as err:
        print(f"[Murmuration] Could not open Starling.blend: {err}")
        return None

    coll = bpy.data.collections.get('Starling collection')
    if coll is None:
        return None

    # Add to scene so particle system can reference it
    existing = [c.name for c in bpy.context.scene.collection.children]
    if coll.name not in existing:
        bpy.context.scene.collection.children.link(coll)

    # Hide source birds — only their particle instances should be visible
    coll.hide_viewport = True
    coll.hide_render   = True

    print("[Murmuration] Starling collection linked and hidden.")
    return coll


# ── 5. GOAL EMPTY ─────────────────────────────────────────────────────────────

def create_goal():
    """
    Plain-axes empty driven by sine/cosine expressions so it traces a
    loose figure-eight volume, pulling the flock with it.

    Driver expressions (Blender scripted drivers — require Auto Run Python):
      X:  sin(frame / 36)  * 10      — sweeps left/right
      Y:  1 + cos(frame / 180) * 15  — slow depth drift in/out of camera
      Z:  3 + cos(frame / 41)  * 6   — bobs up and down
    """
    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    goal = bpy.context.active_object
    goal.name = "goal"

    def _driver(axis_idx, expression):
        fc = goal.driver_add('location', axis_idx)
        fc.driver.type       = 'SCRIPTED'
        fc.driver.expression = expression

    _driver(0, 'sin(frame / 36) * 10')
    _driver(1, '1 + cos(frame / 180) * 15')
    _driver(2, '3 + cos(frame / 41) * 6')

    return goal


# ── 6. PARTICLE SYSTEM ───────────────────────────────────────────────────────

def setup_particle_system(emitter, collection, goal):
    """
    Adds a boid particle system to the emitter and configures all the
    emission, render, movement, and brain-rule settings from the tutorial.

    Rule order (matches tutorial):
      Separate (fuzziness 0.9)
      Goal     (fuzziness 0.9, target = goal empty)
      Average Speed (speed 0.85, wander 0.5, level 0.25)
      Flock    (fuzziness 0.9)
      Avoid Collision
    """
    _set_active(emitter)
    bpy.ops.object.particle_system_add()

    ps       = emitter.particle_systems[0]
    settings = ps.settings
    settings.name = "Murmuration"

    # Emission — all birds generated before frame 1 so simulation is stable
    settings.count       = WORKING_BIRD_COUNT
    settings.frame_start = -1000
    settings.frame_end   = 1
    settings.lifetime    = 2000
    settings.use_render_emitter = False

    # Render
    if collection:
        settings.render_type                = 'COLLECTION'
        settings.instance_collection        = collection
        settings.particle_size              = 0.035
        settings.use_collection_pick_random = True  # varied wing-flap phase
    else:
        settings.render_type = 'HALO'

    # Physics
    settings.physics_type = 'BOIDS'
    settings.mass         = 0.1

    # Movement
    boid = settings.boids
    boid.air_speed_max = 16.0
    boid.air_acc_max   = 0.3
    boid.air_ave_max   = 0.1   # angular velocity

    # "Air personal space" — attribute name varies by version
    for attr in ('air_personal_space', 'land_personal_space'):
        try:
            setattr(boid, attr, 8.0)
            break
        except AttributeError:
            pass

    # Miscellaneous → Pitch
    try:
        boid.pitch = 0.5
    except AttributeError:
        pass

    # ── Boid brain rules ────────────────────────────────────────────────────
    # Default state arrives with [0] Separate, [1] Flock.
    # We delete Flock then re-insert it after Goal and Average Speed.
    state = boid.states[0]
    state.rules[0].factor = 0.9   # Separate fuzziness

    state.active_boid_rule_index = 1   # select Flock
    bpy.ops.boid.rule_del()            # remove it

    for rule_type in ('GOAL', 'AVERAGE_SPEED', 'FLOCK', 'AVOID_COLLISION'):
        bpy.ops.boid.rule_add(type=rule_type)

    # Now configure each rule by type
    for rule in state.rules:
        t = rule.type
        if t == 'SEPARATE':
            rule.factor = 0.9
        elif t == 'GOAL':
            rule.object = goal
            rule.factor = 0.9
        elif t == 'AVERAGE_SPEED':
            try:
                rule.speed  = 0.85
                rule.wander = 0.5
                rule.level  = 0.25
            except AttributeError as err:
                print(f"[Murmuration] Average Speed attribute error: {err}")
        elif t == 'FLOCK':
            rule.factor = 0.9
        elif t == 'AVOID_COLLISION':
            try:
                rule.use_object = True
            except AttributeError:
                pass

    return ps


# ── 7. CAMERA ────────────────────────────────────────────────────────────────

def setup_camera(goal):
    """
    Positions the camera behind/below the action and adds a low-influence
    Track To constraint so it drifts gently toward the goal rather than
    snapping to it.
    """
    cam_obj = next((o for o in bpy.data.objects if o.type == 'CAMERA'), None)
    if cam_obj is None:
        bpy.ops.object.camera_add()
        cam_obj = bpy.context.active_object

    _set_active(cam_obj)
    cam_obj.location       = (0, -50, 0)
    cam_obj.rotation_euler = (math.radians(90), 0, 0)

    for c in list(cam_obj.constraints):
        if c.type == 'TRACK_TO':
            cam_obj.constraints.remove(c)

    track            = cam_obj.constraints.new('TRACK_TO')
    track.target     = goal
    track.track_axis = 'TRACK_NEGATIVE_Z'
    track.up_axis    = 'UP_Y'
    track.influence  = 0.2      # gentle drift, not locked-on

    bpy.context.scene.camera = cam_obj
    return cam_obj


# ── 8. LIGHTS ────────────────────────────────────────────────────────────────

def setup_lights():
    """
    Sun (key light, warm, no shadows) + point light at (24, 10, -10)
    providing warm fill. Both with shadows disabled so the dense flock
    doesn't cast harsh shadow artifacts.
    """
    bpy.ops.object.light_add(type='SUN', location=(0, 0, 10))
    sun = bpy.context.active_object
    sun.name              = "Sun"
    sun.rotation_euler[0] = math.radians(90)
    sun.rotation_euler[2] = math.radians(45)
    sun.data.energy       = 5.0
    sun.data.color        = (1.0, 0.95, 0.85)
    try:
        sun.data.use_shadow = False
    except AttributeError:
        pass

    bpy.ops.object.light_add(type='POINT', location=(24, 10, -10))
    point = bpy.context.active_object
    point.name        = "WarmPoint"
    point.data.energy = 75_000.0
    point.data.color  = (1.0, 0.92, 0.80)
    try:
        point.data.use_shadow = False
    except AttributeError:
        pass


# ── 9. RENDER ────────────────────────────────────────────────────────────────

def setup_render():
    scene = bpy.context.scene
    # EEVEE is sufficient for this scene; handle the 4.x rename
    for engine_id in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            scene.render.engine = engine_id
            break
        except TypeError:
            continue
    scene.render.image_settings.file_format = 'PNG'   # resumable if crash
    scene.frame_start = 1
    scene.frame_end   = 750


# ── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    print("\n[Murmuration] ── Building scene ────────────────────────────────")

    clear_scene()
    print("[Murmuration]  Scene cleared, frame range 1-750")

    emitter = create_emitter()
    print("[Murmuration]  Emitter created at X=-20")

    create_sky()
    print("[Murmuration]  Sky plane created")

    coll = link_starling_collection()

    goal = create_goal()
    print("[Murmuration]  Goal empty created with X/Y/Z drivers")

    setup_particle_system(emitter, coll, goal)
    print("[Murmuration]  Boid particle system configured")

    setup_camera(goal)
    print("[Murmuration]  Camera positioned with track-to (influence 0.2)")

    setup_lights()
    print("[Murmuration]  Sun + point lights added")

    setup_render()
    print("[Murmuration]  Render: EEVEE, PNG, frames 1-750")

    print("\n[Murmuration] ── Done ───────────────────────────────────────────")
    print("[Murmuration]  Next steps:")
    print("   1. Free boid cache: Properties > Particles > Cache > Free All Bakes")
    print("   2. Press Space — birds should swoop toward the goal empty")
    print(f"  3. When satisfied, set Emission > Number to 2500")
    print("   4. Bake the full simulation (Physics > Bake All Dynamics)")
    print("   5. Set Output path, then Render > Render Animation (PNG sequence)")
    if not STARLING_BLEND_PATH:
        print()
        print("[Murmuration]  TIP: set STARLING_BLEND_PATH at top of script to")
        print("   replace particle halos with animated starling meshes.")


main()
