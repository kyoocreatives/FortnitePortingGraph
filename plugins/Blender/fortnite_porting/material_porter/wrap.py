"""Material Porter fork: a wrap laid over what is selected.

A wrap (Assets > Cosmetics > Wraps) has no mesh: its export carries the values its material sets
(the app's ExportContext.WrapValues). The game lays those over a weapon's or vehicle's own
material; here each exact material of the selected objects is built again with them over it
(hook.build_exact's MPWrap), and takes the old one's slots. A material keeps what it was built
with (hook.KEY_OVERLAY), so a style's values stay under the wrap, another wrap replaces the
first, and the wrap can be taken off again. FP's own materials (not exact) are left as they are.
"""
import json

import bpy

from . import build, hook


def targets(context):
    """The selected mesh objects, and the meshes under what else is selected (a weapon's
    armature, a vehicle's empty)."""
    found, seen = [], set()

    def add(o):
        if o.name in seen:
            return
        seen.add(o.name)
        if o.type == 'MESH':
            found.append(o)
        for child in o.children:
            add(child)

    for o in context.selected_objects:
        add(o)
    return found


def lay(job_context, objects, wrap):
    """Each exact material of the objects built with the wrap over it (None: without one).
    (materials changed, objects touched, materials that aren't exact)."""
    rebuilt, touched, skipped = {}, set(), set()
    for o in objects:
        for slot in o.material_slots:
            mat = slot.material
            if mat is None:
                continue
            path = mat.get(build.KEY_PATH)
            if not path:
                skipped.add(mat.name)
                continue
            if wrap is None and not mat.get(hook.KEY_WRAP):
                continue
            new = rebuilt.get(mat.name)
            if new is None:
                data = {"Path": path, "Name": mat.get("OriginalName") or mat.name, "MPWrap": wrap}
                if overlay := mat.get(hook.KEY_OVERLAY):
                    data["MPOverlay"] = json.loads(overlay)
                new = hook.build_exact(job_context, data, obj=o)
                if new is None:
                    skipped.add(mat.name)
                    continue
                for key in ("Hash", "OriginalName", "MPRimLight"):
                    if key in mat and key not in new:
                        new[key] = mat[key]
                rebuilt[mat.name] = new
            if new is not mat:
                slot.material = new
                touched.add(o.name)
    return len(rebuilt), len(touched), sorted(skipped)


def apply_export(import_context, data):
    """A wrap's export (FP's import of it): over the selection. The line for the app's log."""
    wrap = data.get("MPWrap")
    name = data.get("Name") or "wrap"
    if not wrap:
        return "%s has no wrap material" % name
    objects = targets(bpy.context)
    if not objects:
        return "%s: select the weapon (or vehicle) to wrap first" % name
    materials, touched, skipped = lay(import_context, objects, wrap)
    if materials == 0:
        return "%s: nothing to wrap - the selection has no exact materials (%d of FP's own)" % (name, len(skipped))
    line = "%s laid over %d material(s) on %d object(s)" % (name, materials, touched)
    if skipped:
        line += "; %d left as they are (not exact materials)" % len(skipped)
    return line
