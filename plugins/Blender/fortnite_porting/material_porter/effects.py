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
from mathutils import Matrix

KEY = "mp_effect"           # on an effect's objects: "System", "Emitter (GPU)", "Sprite", "Mesh"...
KEY_EMITTER = "mp_emitter"  # on an emitter's empty: the emitter's name
KEY_RENDERER = "mp_renderer"    # on a drawn piece: its renderer's name in the asset
KEY_ROLE = "mp_effect_role"     # on an item's own effect's empty: "trail", "swing", "idle" or "event"
KEY_SKIP = "mp_effect_skip"     # on a piece that isn't drawn: FP's importer hides its material (an anime outline's shell)


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
    if kind == "Decal":
        return _decal(context, fx, name)
    if kind == "Light":
        # a light renderer's piece: a point light (a played one's particles each get their own)
        light = bpy.data.lights.new(name, 'POINT')
        light.energy, light.shadow_soft_size = 5.0, 0.05
        obj = bpy.data.objects.new(name, light)
        obj[KEY] = "Light"
        obj[KEY_RENDERER] = fx.get("Renderer") or ""
        return obj
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


def _decal(context, fx, name):
    """A decal renderer's piece: a 1 m quad across the decal's projection (its local YZ plane: a decal
    projects along its X), with the decal's material. The scene it projects onto isn't here: the
    quad stands for the patch it covers (flat ground under a ground decal)."""
    data = bpy.data.meshes.new(name)
    # facing back along the projection (a ground decal's quad faces up)
    data.from_pydata([(0.0, -0.5, -0.5), (0.0, -0.5, 0.5), (0.0, 0.5, 0.5), (0.0, 0.5, -0.5)], [], [(0, 1, 2, 3)])
    uv = data.uv_layers.new(name="UV0")
    for loop, (x, y) in zip(uv.data, ((0.0, 0.0), (0.0, 1.0), (1.0, 1.0), (1.0, 0.0))):
        loop.uv = (x, y)
    obj = bpy.data.objects.new(name, data)
    obj[KEY] = "Decal"
    obj[KEY_RENDERER] = fx.get("Renderer") or ""
    obj["mp_decal_fade"] = 1.0      # its material's Decal Lifetime Opacity (a played decal's: its particle's)
    data.materials.append(bpy.data.materials.new(name))
    context.import_material(obj.material_slots[0], fx["Material"], {})
    if obj.material_slots[0].material is not None:
        obj.material_slots[0].material.use_backface_culling = False     # a decal has no side: seen from anywhere
    particle_values(obj)
    obj.visible_shadow = False
    return obj


def tag_mesh(obj, fx):
    """An effect's mesh (a mesh renderer's): its particle values."""
    obj[KEY] = "Mesh"
    obj[KEY_RENDERER] = fx.get("Renderer") or ""
    obj["mp_mesh_index"] = int(fx.get("Index") or 0)
    particle_values(obj)
    obj.visible_shadow = False


def on_bone(obj, bone, offset=None):
    """Put an object on a bone (a socket) of the armature it is under: at the bone, following it, with
    the offset the game gives it there (a Blender matrix). False where the armature has no such bone."""
    armature = obj.parent
    if armature is None or armature.type != 'ARMATURE':
        return False
    rest = next((b for b in armature.data.bones if b.name.lower() == bone.lower()), None)
    if rest is None:
        return False
    obj.parent_type = 'BONE'
    obj.parent_bone = rest.name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    # (a bone's children hang from its tail)
    obj.matrix_basis = Matrix.Translation((0.0, -rest.length, 0.0)) @ (offset if offset is not None else Matrix.Identity(4))
    return True


def socket_matrix(socket, scale):
    """Where a skeleton's socket sits on its bone, as a Blender matrix (socket: Location, Rotation, Scale as the app exports them)."""
    from ..processing.utils import make_euler, make_vector
    return Matrix.Translation(make_vector(socket.get("Location"), unreal_coords_correction=True) * scale) \
        @ make_euler(socket.get("Rotation")).to_matrix().to_4x4() @ Matrix.Diagonal((*make_vector(socket.get("Scale")), 1.0))


def _has(rig, name, sockets):
    """Whether an armature can place something on a bone or socket: its own bone, or a socket's bone."""
    bones = {b.name.lower() for b in rig.data.bones}
    socket = {k.lower(): v for k, v in (sockets or {}).items()}.get(name.lower())
    return name.lower() in bones or (socket is not None and str(socket.get("Bone")).lower() in bones)


def lacks_bones(entries, rig, sockets=None):
    """Whether any of an animation's effects sits on a bone or socket the armature can't place."""
    return any(entry.get("SocketName") and not _has(rig, entry["SocketName"], sockets) for entry in entries)


def from_animation(context, entries, rig, sockets=None, skeleton=None):
    """The effects an animation plays (its Niagara notifies): each on the animated armature, on its
    socket with its offsets, replayed from each of its notifies' frames (a timed one kept going
    until its end). skeleton: the game's whole skeleton under the armature, where there is one."""
    from ..processing.utils import time_to_frame
    if not hasattr(context, "collection"):      # (an animation import makes no collection of its own)
        context.collection = rig.users_collection[0] if rig.users_collection else bpy.context.scene.collection
    for entry in entries:
        # on the animated armature; on the game's whole skeleton (animated alike) where there is one: it has
        # every bone an effect may sit on or read
        context.mp_selected_armature = skeleton if skeleton is not None else rig
        mesh = entry.get("Effect") or {}
        fx = mesh.get("MPEffect")
        if fx is None:
            fx = mesh["MPEffect"] = {"Kind": "System"}
        fx["Attach"] = True
        fx["Bone"] = entry.get("SocketName")
        fx["Table"] = sockets or {}
        fx["Offset"] = socket_matrix({"Location": entry.get("LocationOffset"), "Rotation": entry.get("RotationOffset"), "Scale": entry.get("Scale")}, context.scale)
        # its notifies' frames (the animation's: 30 a second), earliest first
        plays = sorted(zip(entry.get("Times") or [0.0], entry.get("Durations") or [0.0]))
        fx["Start"] = time_to_frame(plays[0][0])
        fx["Repeats"] = [time_to_frame(t) - fx["Start"] for t, _ in plays]
        fx["Lengths"] = [time_to_frame(d) if d else 0 for _, d in plays]
        context.import_model(mesh)


def swing(windows, rig):
    """A swing animation's trail windows ([on, off] times) given to the pickaxe effects that show on
    a swing (a pickaxe's own trail and swing effects: on the armature's pickaxe, else any in the
    scene), which are replayed on them."""
    from . import effect_replay
    from .hook import _log
    from ..processing.utils import time_to_frame
    held = [o for o in rig.children_recursive if o.get(KEY) == "System" and o.get(KEY_ROLE) in ("trail", "swing")]
    found = held or [o for o in bpy.context.scene.objects if o.get(KEY) == "System" and o.get(KEY_ROLE) in ("trail", "swing")]
    if not found:
        return
    frames = sorted((time_to_frame(on), time_to_frame(off)) for on, off in windows)
    for root in found:
        root[effect_replay.KEY_START] = frames[0][0]
        root[effect_replay.KEY_REPEATS] = [on - frames[0][0] for on, _ in frames]
        root[effect_replay.KEY_LENGTHS] = [max(off - on, 1) for on, off in frames]
        try:
            for line in effect_replay.play(root):
                _log(line)
        except Exception as e:
            _log("%s: not replayed on the swing (%s: %s)" % (root.name, type(e).__name__, e))
    _log("the swing's %d trail window(s) given to %s%s" % (len(frames), ", ".join(o.name for o in found),
                                                        "" if held else " (no pickaxe under the armature: every pickaxe trail in the scene)"))
    # the held pickaxe's idle effect now moves with the swing: played again on it (its particles were
    # left where the pickaxe was when it was imported)
    for root in [o for o in rig.children_recursive if o.get(KEY) == "System" and o.get(KEY_ROLE) == "idle"] if held else []:
        try:
            for line in effect_replay.play(root):
                _log(line)
        except Exception as e:
            _log("%s: not replayed on the swing (%s: %s)" % (root.name, type(e).__name__, e))


def finish(context, mesh, root):
    """Once a system's tree is imported: its CPU emitters replayed, their pieces played on the particles."""
    fx = mesh.get("MPEffect") or {}
    if fx.get("Kind") != "System":
        return
    from . import effect_replay
    from .hook import _log
    # what FP's importer hides on a character (an anime outline's shell: its material draws ink lines
    # from the scene's depth, which a Blender material can't read) isn't drawn as a white shell here
    hidden = list(getattr(context, "full_vertex_crunch_materials", None) or ())
    for node in root.children:
        for piece in node.children:
            if piece.type == 'MESH' and len(piece.material_slots) and all(any(s.material == m for m in hidden) for s in piece.material_slots):
                piece[KEY_SKIP] = True
                piece.hide_render = piece.hide_viewport = True
                _log("%s: %s not drawn (an outline shell: its lines come from the scene's depth)" % (root.name, piece.name))
    # a contrail, an animation's effect: on the armature selected when it was sent
    rig = getattr(context, "mp_selected_armature", None)
    if fx.get("Attach") and rig is not None:
        effect_replay.attach(root, rig)
    # a pickaxe's own effect, an animation's: on its socket
    bone, offset = mesh.get("MPParentBone") or fx.get("Bone"), fx.get("Offset")
    if offset is None and fx.get("Place"):      # where an item's own effect sits on its socket
        offset = socket_matrix(fx["Place"], context.scale)
    if not bone and offset is not None:
        root.matrix_basis = offset
    table = {k.lower(): v for k, v in (fx.get("Table") or {}).items()}
    if bone and not on_bone(root, bone, offset):
        # a socket the armature doesn't have: on the socket's bone, where the skeleton puts it
        socket = table.get(bone.lower())
        there = socket_matrix(socket, context.scale) @ (offset if offset is not None else Matrix.Identity(4)) if socket is not None else None
        placed = socket is not None and on_bone(root, socket["Bone"], there)
        if not placed and socket is not None and not socket.get("Bone") and root.parent is not None:
            root.matrix_basis = there       # a static mesh's socket: on the mesh itself
            placed = True
        if not placed:
            if offset is not None:
                root.matrix_basis = offset
            _log("%s: no bone or socket %s to put it on: at the armature's origin" % (root.name, bone))
    if role := fx.get("Role"):
        root[KEY_ROLE] = role
    if fx.get("Start") is not None:
        root[effect_replay.KEY_START] = int(fx["Start"])
    if fx.get("Repeats"):
        root[effect_replay.KEY_REPEATS] = [int(x) for x in fx["Repeats"]]
        root[effect_replay.KEY_LENGTHS] = [int(x) for x in fx.get("Lengths") or []]
    if not fx.get("Exports"):
        # nothing of it can be played (GPU emitters only): on something, its still pieces are put out of sight
        if root.parent is not None:
            for node in root.children:
                for piece in node.children:
                    piece.hide_render = piece.hide_viewport = True
            _log("%s: not played (GPU emitters only), its pieces hidden" % root.name)
        return
    try:
        effect_replay.store(root, fx["Exports"], fx.get("Fields"), context.scale, table)
        if fx.get("Sockets"):       # the two sockets a pickaxe's trail runs between
            root[effect_replay.KEY_SOCKETS] = ",".join(fx["Sockets"])
        for name, value in (fx.get("User") or {}).items():
            root[name] = value
        if fx.get("Role") == "event":
            # one the game plays on an event (a weapon's reload, its level up): it waits for Replay Effect
            for node in root.children:
                for piece in node.children:
                    piece.hide_render = piece.hide_viewport = True
            _log("%s: the game plays it on an event: select it and press Replay Effect to play it (from its Start Frame)" % root.name)
            return
        for line in effect_replay.play(root):
            _log(line)
    except Exception as e:      # the pieces stay as imported
        import os
        import traceback
        at = traceback.extract_tb(e.__traceback__)[-1]
        _log("%s: not replayed (%s: %s, at %s:%d)" % (root.name, type(e).__name__, e, os.path.basename(at.filename), at.lineno))
