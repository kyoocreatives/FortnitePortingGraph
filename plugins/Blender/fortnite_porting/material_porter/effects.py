"""Material Porter fork: a particle effect (a Niagara system) in Blender.

The app's export (ExportContext.Effects) is a tree: the system, an empty per emitter, and under
each what its renderers draw. A mesh renderer's meshes come as meshes; a sprite or ribbon
renderer has no mesh, only a material: a 1 m plane is made for it here (facing the scene camera
when the sprite does and there is one).

A CPU emitter's particles are then played (effect_replay: its scripts run over the scene's
frames, and each drawn piece instanced on the particles, frame by frame). A GPU emitter keeps no
script to run: its pieces stay as they are, laid out in a row to pick from.

Every drawn piece carries the values a particle system gives its particles, which the exact
materials read (env.particle_color...): mp_particle = 1 with mp_particle_color (RGBA), mp_dynamic
(four flags: which Dynamic Parameters are set) with mp_dynamic0..3, mp_subimage (a flipbook's
frame), mp_age. On a still piece they are the object's properties, to set by hand; on a played
one, each particle's own (its instance's attributes).
"""
import bpy

KEY = "mp_effect"           # on an effect's objects: "System", "Emitter (GPU)", "Sprite", "Mesh"...
KEY_EMITTER = "mp_emitter"  # on an emitter's empty: the emitter's name
KEY_RENDERER = "mp_renderer"    # on a drawn piece: its renderer's name in the asset


def particle_values(obj):
    """The per-particle values an effect's object carries for its materials."""
    obj["mp_particle"] = 1.0
    obj["mp_particle_color"] = [1.0, 1.0, 1.0, 1.0]
    ui = obj.id_properties_ui("mp_particle_color")
    ui.update(subtype='COLOR', min=0.0, soft_max=1.0, description="Particle Color: what the effect gives each particle")


def make(context, mesh, name):
    """The object for an empty node of an export: an effect's sprite or ribbon as a plane with
    its material, its system and emitters as tagged empties; else a plain empty."""
    fx = mesh.get("MPEffect") or {}
    kind = fx.get("Kind")
    if kind not in ("Sprite", "Ribbon"):
        obj = bpy.data.objects.new(name, None)
        if kind == "Emitter":
            obj[KEY] = "Emitter (%s)" % fx.get("Sim")
            obj[KEY_EMITTER] = name
            obj.empty_display_type = 'SPHERE'
            obj.empty_display_size = 0.1
        elif kind == "System":
            obj[KEY] = "System"
        return obj

    # a 1 m quad in the object's XY plane (a ribbon: 1 m wide, 4 m long), UVs over the whole texture
    # (a flipbook's sub-image is the material's to pick: mp_subimage)
    length = 4.0 if kind == "Ribbon" else 1.0
    data = bpy.data.meshes.new(name)
    data.from_pydata([(-0.5, -length / 2, 0.0), (0.5, -length / 2, 0.0), (0.5, length / 2, 0.0), (-0.5, length / 2, 0.0)], [], [(0, 1, 2, 3)])
    uv = data.uv_layers.new(name="UV0")
    for loop, (x, y) in zip(uv.data, ((0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0))):
        loop.uv = (x, y)
    obj = bpy.data.objects.new(name, data)
    obj[KEY] = kind
    obj[KEY_RENDERER] = fx.get("Renderer") or ""
    data.materials.append(bpy.data.materials.new(name))
    context.import_material(obj.material_slots[0], fx["Material"], {})
    particle_values(obj)
    sub = fx.get("SubImages") or [1.0, 1.0]
    if kind == "Sprite" and sub[0] * sub[1] > 1:
        obj["mp_subimage"] = 0.0
        obj.id_properties_ui("mp_subimage").update(min=0.0, max=sub[0] * sub[1] - 1, step=100, description="The flipbook's sub-image the sprite shows")
    obj.visible_shadow = False
    camera = bpy.context.scene.camera
    if kind == "Sprite" and "Camera" in str(fx.get("Facing")) and camera is not None:
        track = obj.constraints.new('TRACK_TO')
        track.target = camera
        track.track_axis = 'TRACK_Z'
        track.up_axis = 'UP_Y'
    return obj


def tag_mesh(obj, fx):
    """An effect's mesh (a mesh renderer's): its particle values."""
    obj[KEY] = "Mesh"
    obj[KEY_RENDERER] = fx.get("Renderer") or ""
    obj["mp_mesh_index"] = int(fx.get("Index") or 0)
    particle_values(obj)
    obj.visible_shadow = False


def finish(context, mesh, root):
    """Once a system's tree is imported: its CPU emitters replayed, their pieces played on the particles."""
    fx = mesh.get("MPEffect") or {}
    if fx.get("Kind") != "System" or not fx.get("Exports"):
        return
    from . import effect_replay
    from .hook import _log
    try:
        for line in effect_replay.play(root, fx["Exports"], fx.get("Fields"), context.scale):
            _log(line)
    except Exception as e:      # the pieces stay as imported
        import os
        import traceback
        at = traceback.extract_tb(e.__traceback__)[-1]
        _log("%s: not replayed (%s: %s, at %s:%d)" % (root.name, type(e).__name__, e, os.path.basename(at.filename), at.lineno))
