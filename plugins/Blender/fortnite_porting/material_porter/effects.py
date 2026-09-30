"""Material Porter fork: a particle effect (a Niagara system) as what it is made of.

The app's export (ExportContext.Effects) is a tree: the system, an empty per emitter, and under
each what its renderers draw. A mesh renderer's meshes come as meshes; a sprite or ribbon
renderer has no mesh, only a material: a 1 m plane is made for it here (facing the scene camera
when the sprite does and there is one). Every drawn object carries the values a particle system
would give its particles, which the exact materials read (env.particle_color): mp_particle = 1
and mp_particle_color (RGBA), to set by hand. A material's Dynamic Parameters keep their own
defaults unless the object says otherwise (mp_dynamic = 1 with mp_dynamic0..3, four floats each).
"""
import bpy

KEY = "mp_effect"       # on an effect's objects: "Emitter (GPU)", "Sprite", "Mesh"...


def particle_values(obj):
    """The per-particle values an effect's object carries for its materials."""
    obj["mp_particle"] = 1.0
    obj["mp_particle_color"] = [1.0, 1.0, 1.0, 1.0]
    ui = obj.id_properties_ui("mp_particle_color")
    ui.update(subtype='COLOR', min=0.0, soft_max=1.0, description="Particle Color: what the effect gives each particle")


def make(context, mesh, name):
    """The object for an empty node of an export: an effect's sprite or ribbon as a plane with
    its material, its emitter as a tagged empty; else a plain empty."""
    fx = mesh.get("MPEffect") or {}
    kind = fx.get("Kind")
    if kind not in ("Sprite", "Ribbon"):
        obj = bpy.data.objects.new(name, None)
        if kind == "Emitter":
            obj[KEY] = "Emitter (%s)" % fx.get("Sim")
            obj.empty_display_type = 'SPHERE'
            obj.empty_display_size = 0.1
        return obj

    # a 1 m quad in the object's XY plane (a ribbon: 1 m wide, 4 m long), UVs over the whole texture
    # (or its first sub-image: a flipbook's first frame)
    length = 4.0 if kind == "Ribbon" else 1.0
    data = bpy.data.meshes.new(name)
    data.from_pydata([(-0.5, -length / 2, 0.0), (0.5, -length / 2, 0.0), (0.5, length / 2, 0.0), (-0.5, length / 2, 0.0)], [], [(0, 1, 2, 3)])
    sub = fx.get("SubImages") or [1.0, 1.0]
    u, v = 1.0 / max(sub[0], 1.0), 1.0 / max(sub[1], 1.0)
    uv = data.uv_layers.new(name="UV0")
    for loop, (x, y) in zip(uv.data, ((0.0, 1.0 - v), (u, 1.0 - v), (u, 1.0), (0.0, 1.0))):
        loop.uv = (x, y)
    obj = bpy.data.objects.new(name, data)
    obj[KEY] = kind
    data.materials.append(bpy.data.materials.new(name))
    context.import_material(obj.material_slots[0], fx["Material"], {})
    particle_values(obj)
    obj.visible_shadow = False
    camera = bpy.context.scene.camera
    if kind == "Sprite" and "Camera" in str(fx.get("Facing")) and camera is not None:
        track = obj.constraints.new('TRACK_TO')
        track.target = camera
        track.track_axis = 'TRACK_Z'
        track.up_axis = 'UP_Y'
    return obj


def tag_mesh(obj):
    """An effect's mesh (a mesh renderer's): its particle values."""
    obj[KEY] = "Mesh"
    particle_values(obj)
    obj.visible_shadow = False
