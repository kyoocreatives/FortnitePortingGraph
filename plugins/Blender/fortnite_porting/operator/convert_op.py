"""Material Porter fork: FP imports made before (with FortnitePorting itself, or with the fork's
own FP shaders) converted to the fork's exact materials.

An FP material keeps only its asset's name ("OriginalName"); the app's bridge finds the
material assets with that name (find-materials) and the fork's material import builds the
exact one (material_porter.hook.build_exact), which then takes the FP material's slots.
Where two assets share the name, the one whose textures the FP material shows wins. A
style's values FP put over the material (a colour swap, a building's texture data) aren't
kept on the FP material, so the exact one has the asset's own.
"""
import re
import types

import bpy

from ..material_porter import build, hook
from ..material_porter.app_client import AppClient, AppError

# names per find-materials request (they go in the URL)
CHUNK = 40


def _asset_name(mat):
    """The material asset's name: FP keeps it; else the Blender name without FP's style
    hash (MI_X_3e6eed05) and Blender's duplicate number (.001)."""
    return mat.get("OriginalName") or re.sub(r"(_[0-9a-f]{8})?(\.\d{3})?$", "", mat.name)


def _from_fp(mat):
    """An FP import's material: it keeps its hash and asset name; an older import's,
    named as UE names materials."""
    return "Hash" in mat or "OriginalName" in mat or re.match(r"(M|MI|MM|MAT|MIC)_", mat.name) is not None


def _image_names(mat):
    """The images an FP material shows, by texture name."""
    names = set()
    if not mat.node_tree:
        return names
    todo, seen = [mat.node_tree], set()
    while todo:
        tree = todo.pop()
        if tree.name in seen:
            continue
        seen.add(tree.name)
        for n in tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                names.add(re.sub(r"(\.\d{3})?(\.[a-z]{3,4})?$", "", n.image.name, flags=re.I).lower())
            elif n.type == 'GROUP' and n.node_tree and not n.node_tree.name.startswith("FPv4"):
                todo.append(n.node_tree)
    return names


def _pick(app, mat, paths):
    """The candidate whose own textures the FP material shows most of (the first on a tie)."""
    if len(paths) == 1:
        return paths[0]
    shown = _image_names(mat)
    best, score = paths[0], -1
    for path in paths:
        try:
            textures = (app.get("material", path).get("textures") or {}).values()
        except AppError:
            continue
        s = sum(1 for t in textures if t.split("/")[-1].split(".")[0].lower() in shown)
        if s > score:
            best, score = path, s
    return best


class FPMP_OT_ConvertExact(bpy.types.Operator):
    """Rebuild the FP materials of these objects as the fork's exact materials (the FP app must be running)"""
    bl_idname = "fpmp.convert_exact"
    bl_label = "Convert to Exact Materials"
    bl_options = {'REGISTER', 'UNDO'}

    scope: bpy.props.EnumProperty(
        name="Objects",
        items=[('SELECTED', "Selected", "The selected objects' materials"),
               ('SCENE', "Scene", "Every object's materials in the scene")],
        default='SELECTED')
    rim_light: bpy.props.BoolProperty(
        name="Rim Light", default=False,
        description="Keep the rim light of Fortnite's character materials (as the Blender export setting)")

    def execute(self, context):
        if bpy.app.version < (5, 0, 0):
            self.report({'ERROR'}, "Exact materials need Blender 5.0 or newer")
            return {'CANCELLED'}
        objects = context.selected_objects if self.scope == 'SELECTED' else context.scene.objects
        # FP material -> the slots that show it
        slots = {}
        for obj in objects:
            for i, slot in enumerate(getattr(obj, "material_slots", [])):
                mat = slot.material
                if mat is None or build.KEY_PATH in mat or not _from_fp(mat):
                    continue
                slots.setdefault(mat, []).append((obj, i))
        if not slots:
            self.report({'INFO'}, "No FP materials here (they are exact already, or not FortnitePorting's)")
            return {'CANCELLED'}

        app = AppClient(hook.URL)
        names = sorted({_asset_name(m) for m in slots})
        found = {}
        try:
            for k in range(0, len(names), CHUNK):
                found.update(app.get("find-materials", ",".join(names[k:k + CHUNK])))
        except AppError as e:
            self.report({'ERROR'}, "The FP app isn't answering: open FortnitePorting MP and load the game (%s)" % e)
            return {'CANCELLED'}

        # one import session for the lot: many materials lay out lazily, as a world's do
        job = types.SimpleNamespace(options={"RimLight": self.rim_light},
                                    type=types.SimpleNamespace(name="WORLD" if len(slots) > 20 else "MESH"))
        converted, missing, failed = 0, [], []
        wm = context.window_manager
        wm.progress_begin(0, len(slots))
        for n, (mat, users) in enumerate(slots.items()):
            wm.progress_update(n)
            name = _asset_name(mat)
            paths = found.get(name) or []
            if not paths:
                missing.append(name)
                continue
            exact = hook.build_exact(job, {"Path": _pick(app, mat, paths), "Name": name}, obj=users[0][0])
            if exact is None:
                failed.append(name)
                continue
            for key in ("Hash", "OriginalName"):
                if key in mat:
                    exact[key] = mat[key]
            exact["MPRimLight"] = self.rim_light
            for obj, i in users:
                obj.material_slots[i].material = exact
            converted += 1
        wm.progress_end()

        msg = "%d material%s converted" % (converted, "" if converted == 1 else "s")
        if missing:
            msg += "; %d not found in the game (%s)" % (len(missing), ", ".join(missing[:5]) + (" ..." if len(missing) > 5 else ""))
        if failed:
            msg += "; %d not built, kept as FP's (see the log: %s)" % (len(failed), ", ".join(failed[:5]) + (" ..." if len(failed) > 5 else ""))
        self.report({'WARNING'} if missing or failed else {'INFO'}, msg)
        return {'FINISHED'}


class FPMP_OT_RemoveWrap(bpy.types.Operator):
    """Take the wrap off the selected objects' materials (the one picked on the asset's page in the
    app, or a weapon's own), with the FP app open"""
    bl_idname = "fpmp.remove_wrap"
    bl_label = "Remove Wrap"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        from ..material_porter import wrap
        job = types.SimpleNamespace(options={'RimLight': False}, type=types.SimpleNamespace(name='MESH'))
        materials, objects, _ = wrap.lay(job, wrap.targets(context), None)
        if materials == 0:
            self.report({'INFO'}, "No wrapped materials on the selection")
            return {'CANCELLED'}
        self.report({'INFO'}, "Wrap removed from %d material(s) on %d object(s)" % (materials, objects))
        return {'FINISHED'}


class FPMP_OT_ReplayEffect(bpy.types.Operator):
    """Replay the selected particle effects over the scene's frame range, with their User properties.
    With an armature selected too, the effects are put on it first: they read its bones and sockets,
    and follow its animation (a contrail on a character, a trail on a swinging pickaxe)"""
    bl_idname = "fpmp.replay_effect"
    bl_label = "Replay Effect"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        from ..material_porter import effect_replay
        roots = effect_replay.roots(context.selected_objects)
        if not roots:
            self.report({'INFO'}, "Select an imported effect (its empty, or anything under it)")
            return {'CANCELLED'}
        rigs = [o for o in context.selected_objects if o.type == 'ARMATURE']
        rig = context.active_object if context.active_object in rigs else rigs[0] if len(rigs) == 1 else None
        for root in roots:
            if rig is not None and effect_replay.rig_of(root) is not rig:
                effect_replay.attach(root, rig)
            for line in effect_replay.play(root):
                self.report({'INFO'}, line)
        return {'FINISHED'}


class FPMP_OT_CreatureRig(bpy.types.Operator):
    """Give the selected armature a creature rig: IK legs (a foot control and a knee pole each), FK spine,
    tail, head and the rest, read from its skeleton (a wolf's, a chicken's, a raptor's...)"""
    bl_idname = "fpmp.creature_rig"
    bl_label = "Rig Creature"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        obj = context.active_object
        return obj is not None and obj.type == 'ARMATURE' and not obj.data.get("is_creature_rig") and not obj.data.get("is_tasty")

    def execute(self, context):
        from ..processing.context.creature_rig import create
        self.report({'INFO'}, create(context.active_object))
        return {'FINISHED'}


class FPMP_OT_VehicleRig(bpy.types.Operator):
    """Give the selected armature a vehicle rig: CR_Main places it, CR_Drive moved forward drives it (the
    wheels spin by the distance), CR_Steer turns the front wheels and the steering wheel"""
    bl_idname = "fpmp.vehicle_rig"
    bl_label = "Rig Vehicle"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        obj = context.active_object
        return obj is not None and obj.type == 'ARMATURE' and not obj.data.get("is_vehicle_rig") and not obj.data.get("is_creature_rig") and not obj.data.get("is_tasty")

    def execute(self, context):
        from ..processing.context.vehicle_rig import create
        self.report({'INFO'}, create(context.active_object))
        return {'FINISHED'}


class FPMP_PT_CreatureRig(bpy.types.Panel):
    bl_label = "Rig"
    bl_idname = "FPMP_PT_creature_rig"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Fortnite Porting"

    @classmethod
    def poll(cls, context):
        return context.active_object is not None and context.active_object.type == 'ARMATURE'

    def draw(self, context):
        obj = context.active_object
        col = self.layout.column(align=True)
        if obj.data.get("is_vehicle_rig"):
            # arrow: move forward; arcs: rotate; roof slab: move/tilt; wheel rings: lift/turn
            col.label(text="Drive: arrow. Steer, drift: arcs. Body: roof")
            col.prop(obj, "fpmp_ground", text="Ground")             # the wheels follow it
            col.prop(obj, '["auto_wheels"]', text="Wheels Spin", slider=True)
            col.prop(obj, '["auto_steer"]', text="Wheels Steer", slider=True)
            for key, text in (("countersteer", "Counter-steer"), ("suspension", "Body Follows Wheels"), ("lean", "Lean in Turns")):
                if key in obj:          # (a rig made before they were)
                    col.prop(obj, '["%s"]' % key, text=text, slider=True)
            return
        if not obj.data.get("is_creature_rig"):
            col.operator(FPMP_OT_CreatureRig.bl_idname)
            col.operator(FPMP_OT_VehicleRig.bl_idname)
            return
        # each limb's IK (0: FK, as an animation plays it); the eyes' aim
        col.label(text="IK (0 to play an animation):")
        for key in sorted(k for k in obj.keys() if k.startswith("ik_")):
            col.prop(obj, '["%s"]' % key, text=key[3:], slider=True)
        if "eyes_aim" in obj:
            col.prop(obj, '["eyes_aim"]', text="Eyes Aim", slider=True)


class FPMP_PT_Exact(bpy.types.Panel):
    bl_label = "Exact Materials"
    bl_idname = "FPMP_PT_exact"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Fortnite Porting"

    def draw(self, context):
        col = self.layout.column(align=True)
        col.label(text="Rebuild FP materials exactly:")
        row = col.row(align=True)
        row.operator(FPMP_OT_ConvertExact.bl_idname, text="Selected").scope = 'SELECTED'
        row.operator(FPMP_OT_ConvertExact.bl_idname, text="Scene").scope = 'SCENE'
        # a wrap goes on in the app (the Wrap list of a weapon's or vehicle's page); it comes off here
        col.separator()
        col.operator(FPMP_OT_RemoveWrap.bl_idname, text="Remove Wrap from Selected")
        # an effect is replayed at import; again here: on a character, over another frame range
        col.separator()
        col.operator(FPMP_OT_ReplayEffect.bl_idname, text="Replay Effect")


classes = (FPMP_OT_ConvertExact, FPMP_OT_RemoveWrap, FPMP_OT_ReplayEffect, FPMP_PT_Exact, FPMP_OT_CreatureRig, FPMP_OT_VehicleRig, FPMP_PT_CreatureRig)


def register():
    from ..processing.context import vehicle_rig
    for c in classes:
        bpy.utils.register_class(c)
    vehicle_rig.register()          # (the armature object's Ground)


def unregister():
    from ..processing.context import vehicle_rig
    vehicle_rig.unregister()
    for c in reversed(classes):
        bpy.utils.unregister_class(c)
