bl_info = {
    "name": "Character FBX Exporter",
    "author": "chickenandribs",
    "version": (1, 0, 0),
    "blender": (4, 3, 0),
    "location": "View3D > Sidebar > FTR Export",
    "description": "One-button FBX export for the FTR/Unity character pipeline, "
                    "with the correct transform settings hardcoded so they can't "
                    "drift between exports.",
    "category": "Import-Export",
}

import bpy
import os

# ── Why this exists ──────────────────────────────────────────────────────
# Every Unity import problem we hit that traced back to Blender (character
# coming in 8000x too large, tipped on his back, animation clips becoming
# huge/distorted) came from the FBX export dialog's own remembered settings
# drifting between sessions — a dropdown reverting to a different default,
# a checkbox getting toggled while looking for something else. This add-on
# removes the dialog entirely: pick a mode, type a name, click Export. The
# transform settings below are fixed and cannot be changed from the UI.
#
# Two modes:
#   Base Model    — Mesh + Armature, rest pose only, no animation. Use this
#                    once per character (or whenever the rig itself changes).
#   Animation Clip — Armature only, no mesh, animation baked over the
#                    scene's current Start/End frame range. Use this for
#                    every individual clip (idle, walk, sit, etc.).
#
# Animation-only exports are deliberately mesh-less to stay small — the
# Unity-side character_import_pipeline tool shares one Avatar per character
# across all of them, so the missing mesh here is expected, not a bug.

FIXED_EXPORT_KWARGS = dict(
    apply_scale_options='FBX_SCALE_NONE',  # "All Local" — bakes scale into
                                            # geometry instead of the FBX's
                                            # own embedded scale metadata,
                                            # which Unity has repeatedly
                                            # proven unreliable about trusting.
    axis_forward='-Z',
    axis_up='Y',
    apply_unit_scale=True,
    bake_space_transform=False,            # "Apply Transform" — deliberately
                                            # off; Blender's own exporter flags
                                            # this as risky for anything other
                                            # than messy nested-scale rigs.
    mesh_smooth_type='FACE',
    add_leaf_bones=False,
)


class FTRExportSettings(bpy.types.PropertyGroup):
    export_path: bpy.props.StringProperty(
        name="Export To",
        description="Full output path for the .fbx file",
        subtype='FILE_PATH',
    )
    export_mode: bpy.props.EnumProperty(
        name="Mode",
        items=[
            ('BASE_MODEL', "Base Model", "Mesh + Armature, rest pose, no animation"),
            ('ANIMATION_CLIP', "Animation Clip", "Armature only, animation baked from Start/End frame"),
        ],
        default='BASE_MODEL',
    )


class FTR_OT_export_character_fbx(bpy.types.Operator):
    bl_idname = "ftr_export.character_fbx"
    bl_label = "Export"
    bl_description = "Export using the fixed FTR/Unity transform settings"

    def execute(self, context):
        settings = context.scene.ftr_export_settings
        path = bpy.path.abspath(settings.export_path)

        if not path:
            self.report({'ERROR'}, "Set an export path first.")
            return {'CANCELLED'}

        if not path.lower().endswith(".fbx"):
            path += ".fbx"

        out_dir = os.path.dirname(path)
        if out_dir and not os.path.isdir(out_dir):
            os.makedirs(out_dir, exist_ok=True)

        kwargs = dict(FIXED_EXPORT_KWARGS)

        if settings.export_mode == 'BASE_MODEL':
            kwargs.update(
                object_types={'EMPTY', 'ARMATURE', 'MESH'},
                bake_anim=False,
            )
        else:  # ANIMATION_CLIP
            # Mesh is included here too, even though only the armature
            # actually needs to move — Unity 2018.2 predates the scripting
            # API for sharing one Avatar across files (added in 2019.3), and
            # its "Create From This Model" bone auto-detection is unreliable
            # without skin-weight data to disambiguate bone roles (fails with
            # "Required human bone 'Hips' not found" on an armature-only
            # file). Keeping mesh in every export costs almost nothing here —
            # these lean Blender exports run under 1MB either way — and keeps
            # every file self-sufficient instead of depending on import order.
            #
            # No explicit start/end kwargs exist on this operator — baking
            # already uses the scene's current Start/End frame range by
            # default, which is why the panel below tells you to set those
            # before exporting rather than entering a range here.
            kwargs.update(
                object_types={'EMPTY', 'ARMATURE', 'MESH'},
                bake_anim=True,
                bake_anim_use_all_actions=False,
                bake_anim_use_nla_strips=False,
                bake_anim_force_startend_keying=True,
                bake_anim_step=1.0,
            )

        bpy.ops.export_scene.fbx(filepath=path, **kwargs)

        self.report({'INFO'}, f"Exported {settings.export_mode} to {path}")
        return {'FINISHED'}


class FTR_PT_export_panel(bpy.types.Panel):
    bl_label = "FTR Character Export"
    bl_idname = "FTR_PT_export_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "FTR Export"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.ftr_export_settings

        layout.prop(settings, "export_mode", expand=True)
        layout.prop(settings, "export_path")

        if settings.export_mode == 'ANIMATION_CLIP':
            scene = context.scene
            box = layout.box()
            box.label(text=f"Frame range: {scene.frame_start} – {scene.frame_end}", icon='TIME')
            box.label(text="(set via the Timeline's Start/End fields)")

        layout.separator()
        layout.operator(FTR_OT_export_character_fbx.bl_idname, icon='EXPORT')


_classes = (
    FTRExportSettings,
    FTR_OT_export_character_fbx,
    FTR_PT_export_panel,
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.ftr_export_settings = bpy.props.PointerProperty(type=FTRExportSettings)


def unregister():
    del bpy.types.Scene.ftr_export_settings
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
