"""Material Porter fork: a particle effect's CPU emitters, played in Blender.

The emitters' scripts are run over the scene's frame range (niagara.py), and what each frame
holds is kept: a mesh of points per drawn piece, every frame's particles in it, each point with
its frame and its particle's values. A geometry nodes modifier keeps the current frame's points
and puts the piece (the sprite's plane, the mesh renderer's mesh) on each: turned to the camera
or along its velocity as the renderer says, sized, and carrying the particle's colour and
material values as instance attributes, which the exact materials read.

The replay is the engine's at 60 ticks a second or so (a whole number of ticks per frame), the
effect's owner still at the origin. It stops where the system completes; an effect that never
does fills the scene's frame range.
"""
import bpy
import numpy as np

from . import effects, niagara

GROUP = "MP Effect Particles"
RIBBONS = "MP Effect Ribbons"
GROUP_VERSION = 3
# how a particle's piece is turned (the modifier's Turn): as the renderer says
TURN_OWN, TURN_CAMERA, TURN_CAMERA_VELOCITY, TURN_FACING, TURN_MESH_VELOCITY, TURN_MESH_CAMERA = range(6)
TURNS = "0: the particle's own rotation (a mesh); 1: a sprite facing the camera; 2: a sprite facing the camera, its length " \
        "along the velocity; 3: a sprite facing the particle's own direction; 4: a mesh, its X axis along the velocity; " \
        "5: a mesh, its X axis to the camera"
MOST_POINTS = 2_000_000     # over all frames, per effect


def replay(exports, fields, fps, frames, camera=None):
    """The system ticked through the frames: (system, {emitter name: [(floats, ints) per frame]})."""
    system = niagara.System(exports, fields=fields)
    if camera is not None:
        system.camera = camera
    ticks = max(1, round(60.0 / fps))
    dt = 1.0 / (fps * ticks)
    props = system.props
    warm = int(props.get("WarmupTickCount") or 0)
    for _ in range(min(warm, 600)):
        system.tick(float(props.get("WarmupTickDelta") or 1.0 / 15.0))
    tracks = {e.name: [] for e in system.emitters}
    total = 0
    for _ in range(frames):
        for _ in range(ticks):
            system.tick(dt)
        for e in system.emitters:
            n = e.data.count
            tracks[e.name].append((e.data.floats[:, :n].copy(), e.data.ints[:, :n].copy()))
            total += n
        if system.done or total > MOST_POINTS:
            break
    return system, tracks


class Track:
    """An emitter's particles over the frames, an attribute at a time."""

    def __init__(self, emitter, frames):
        self.layout, self.frames = emitter.layout, frames
        self.counts = np.array([f[0].shape[1] for f in frames], np.int64)
        self.total = int(self.counts.sum())
        self.frame = np.repeat(np.arange(len(frames), dtype=np.int32), self.counts)

    def has(self, name):
        return name in self.layout.vars

    def get(self, name, default):
        """The attribute over all frames (particles x components); the default where the emitter has none."""
        v = self.layout.vars.get(name)
        if v is None:
            return np.tile(np.asarray(default, np.float32), (self.total, 1))
        _, f0, i0, nf, ni = v
        rows = [f[0][f0:f0 + nf] if nf else f[1][i0:i0 + ni] for f in self.frames]
        return np.concatenate(rows, axis=1).T


def bound(renderer, binding, default):
    """The particle attribute a renderer reads for a binding: its own choice, else the usual one."""
    name = ((renderer.get(binding) or {}).get("ParamMapVariable") or {}).get("Name") or ""
    return name[len("Particles."):] if name.startswith("Particles.") else default


def _attribute(mesh, name, kind, values):
    field = {"FLOAT_VECTOR": "vector", "FLOAT_COLOR": "color"}.get(kind, "value")
    a = mesh.attributes.new(name, kind, 'POINT')
    a.data.foreach_set(field, np.ascontiguousarray(values).ravel())


def points(name, track, renderer, kind, scale, keep=None):
    """A mesh of points: every frame's particles, with what the piece's instances (a ribbon's curve)
    and materials take of each."""
    pick = (lambda a: a[keep]) if keep is not None else (lambda a: a)
    flip = np.array([1.0, -1.0, 1.0], np.float32)       # UE's Y runs the other way
    position = pick(track.get(bound(renderer, "PositionBinding", "Position"), (0, 0, 0))) * flip * scale
    mesh = bpy.data.meshes.new(name)
    mesh.vertices.add(len(position))
    mesh.vertices.foreach_set("co", np.ascontiguousarray(position, np.float32).ravel())
    _attribute(mesh, "mp_frame", 'INT', pick(track.frame))
    _attribute(mesh, "mp_velocity", 'FLOAT_VECTOR', pick(track.get(bound(renderer, "VelocityBinding", "Velocity"), (0, 0, 0))) * flip)
    if kind == "Ribbon":
        # a ribbon runs through its particles in link order (their age where they have none), one ribbon per ID
        _attribute(mesh, "mp_width", 'FLOAT', pick(track.get(bound(renderer, "RibbonWidthBinding", "RibbonWidth"), (1,))) * scale)
        order = bound(renderer, "RibbonLinkOrderBinding", "RibbonLinkOrder")
        _attribute(mesh, "mp_order", 'FLOAT', pick(track.get(order if track.has(order) else bound(renderer, "NormalizedAgeBinding", "NormalizedAge"), (0,))))
        _attribute(mesh, "mp_ribbon", 'INT', pick(track.get(bound(renderer, "RibbonIdBinding", "RibbonID"), (0,)))[:, 0].astype(np.int32))
        _attribute(mesh, "mp_facing", 'FLOAT_VECTOR', pick(track.get(bound(renderer, "RibbonFacingBinding", "RibbonFacing"), (0, 0, 1))) * flip)
    elif kind == "Sprite":
        size = pick(track.get(bound(renderer, "SpriteSizeBinding", "SpriteSize"), (50, 50)))
        _attribute(mesh, "mp_size", 'FLOAT_VECTOR', np.concatenate([size, np.zeros((len(size), 1), np.float32)], axis=1))   # in UE's units, for the materials
        size = size * scale
        _attribute(mesh, "mp_facing", 'FLOAT_VECTOR', pick(track.get(bound(renderer, "SpriteFacingBinding", "SpriteFacing"), (1, 0, 0))) * flip)
        _attribute(mesh, "mp_scale", 'FLOAT_VECTOR', np.concatenate([size, np.ones((len(size), 1), np.float32)], axis=1))
        _attribute(mesh, "mp_spin", 'FLOAT', -np.radians(pick(track.get(bound(renderer, "SpriteRotationBinding", "SpriteRotation"), (0,)))))
    else:
        _attribute(mesh, "mp_scale", 'FLOAT_VECTOR', pick(track.get(bound(renderer, "ScaleBinding", "Scale"), (1, 1, 1))))
        q = pick(track.get(bound(renderer, "MeshOrientationBinding", "MeshOrientation"), (0, 0, 0, 1))).astype(np.float32)
        length = np.linalg.norm(q, axis=1, keepdims=True)
        q = np.where(length > 1e-8, q / np.maximum(length, 1e-8), np.array([0, 0, 0, 1], np.float32))
        # UE's (x, y, z, w) to Blender's (w, x, y, z), Y flipped
        _attribute(mesh, "mp_rotation", 'QUATERNION', np.stack([q[:, 3], -q[:, 0], q[:, 1], -q[:, 2]], axis=1))
    # what the materials read
    _attribute(mesh, "mp_particle", 'FLOAT', np.ones(len(position), np.float32))
    _attribute(mesh, "mp_particle_color", 'FLOAT_COLOR', pick(track.get(bound(renderer, "ColorBinding", "Color"), (1, 1, 1, 1))))
    flags, valid = [], int(renderer.get("MaterialParamValidMask") or 0)     # four bits per parameter: the channels the emitter writes
    for i, (binding, usual) in enumerate((("DynamicMaterialBinding", "DynamicMaterialParameter"), ("DynamicMaterial1Binding", "DynamicMaterialParameter1"),
                                         ("DynamicMaterial2Binding", "DynamicMaterialParameter2"), ("DynamicMaterial3Binding", "DynamicMaterialParameter3"))):
        attribute = bound(renderer, binding, usual)
        flags.append(1.0 if track.has(attribute) and valid >> 4 * i & 15 else 0.0)
        if flags[-1]:
            _attribute(mesh, "mp_dynamic%d" % i, 'FLOAT_COLOR', pick(track.get(attribute, (1, 1, 1, 1))))
    _attribute(mesh, "mp_dynamic", 'FLOAT_COLOR', np.tile(np.array(flags, np.float32), (len(position), 1)))
    _attribute(mesh, "mp_subimage", 'FLOAT', pick(track.get(bound(renderer, "SubImageIndexBinding", "SubImageIndex"), (0,))))
    _attribute(mesh, "mp_age", 'FLOAT', pick(track.get(bound(renderer, "NormalizedAgeBinding", "NormalizedAge"), (0,))))
    return mesh


def group():
    """The node group that plays a points mesh: the current frame's points, the piece on each."""
    g = bpy.data.node_groups.get(GROUP)
    if g is not None and g.get("mp_version") == GROUP_VERSION:
        return g
    g = bpy.data.node_groups.new(GROUP, 'GeometryNodeTree')
    g["mp_version"] = GROUP_VERSION
    g.is_modifier = True
    face = g.interface
    face.new_socket(name="Geometry", in_out='INPUT', socket_type='NodeSocketGeometry')
    face.new_socket(name="Geometry", in_out='OUTPUT', socket_type='NodeSocketGeometry')
    face.new_socket(name="Piece", in_out='INPUT', socket_type='NodeSocketObject', description="What each particle draws")
    face.new_socket(name="Start Frame", in_out='INPUT', socket_type='NodeSocketInt', description="The scene frame the effect starts on").default_value = 1
    face.new_socket(name="Frames", in_out='INPUT', socket_type='NodeSocketInt', description="How many frames were replayed").default_value = 1
    face.new_socket(name="Loop", in_out='INPUT', socket_type='NodeSocketBool', description="Start over after the last replayed frame")
    turn = face.new_socket(name="Turn", in_out='INPUT', socket_type='NodeSocketInt', description=TURNS)
    turn.min_value, turn.max_value = 0, 5
    face.new_socket(name="Piece Rotation", in_out='INPUT', socket_type='NodeSocketRotation', description="The renderer's own rotation of the piece")
    face.new_socket(name="Piece Scale", in_out='INPUT', socket_type='NodeSocketVector', description="The renderer's own scale of the piece").default_value = (1.0, 1.0, 1.0)

    nodes, links = g.nodes, g.links
    column = [0]

    def node(kind, **settings):
        n = nodes.new(kind)
        n.location = (column[0], 0)
        column[0] += 190
        for k, v in settings.items():
            setattr(n, k, v)
        return n

    def attribute(name, kind):
        n = node("GeometryNodeInputNamedAttribute", data_type=kind)
        n.inputs["Name"].default_value = name
        return n.outputs[0]

    def math_(op, a, b):
        n = node("ShaderNodeMath", operation=op)
        for socket, v in zip(n.inputs, (a, b)):
            if hasattr(v, "is_linked"):
                links.new(v, socket)
            else:
                socket.default_value = v
        return n.outputs[0]

    inputs, outputs = node("NodeGroupInput"), nodes.new("NodeGroupOutput")
    # the frame of the replay the scene is on
    time = node("GeometryNodeInputSceneTime")
    index = math_('SUBTRACT', time.outputs["Frame"], inputs.outputs["Start Frame"])
    looped = math_('FLOORED_MODULO', index, inputs.outputs["Frames"])
    which = node("GeometryNodeSwitch", input_type='FLOAT')
    links.new(inputs.outputs["Loop"], which.inputs["Switch"])
    links.new(index, which.inputs["False"])
    links.new(looped, which.inputs["True"])
    other = node("FunctionNodeCompare", data_type='FLOAT', operation='NOT_EQUAL')
    links.new(attribute("mp_frame", 'INT'), other.inputs[0])
    links.new(which.outputs[0], other.inputs[1])
    other.inputs["Epsilon"].default_value = 0.5
    current = node("GeometryNodeDeleteGeometry", domain='POINT')
    links.new(inputs.outputs["Geometry"], current.inputs["Geometry"])
    links.new(other.outputs[0], current.inputs["Selection"])

    # the piece, with the renderer's own rotation and scale
    piece = node("GeometryNodeObjectInfo", transform_space='ORIGINAL')
    links.new(inputs.outputs["Piece"], piece.inputs["Object"])
    placed = node("GeometryNodeTransform")
    links.new(piece.outputs["Geometry"], placed.inputs["Geometry"])
    links.new(inputs.outputs["Piece Rotation"], placed.inputs["Rotation"])
    links.new(inputs.outputs["Piece Scale"], placed.inputs["Scale"])
    # UE turns a particle mesh's normals by its scale, not by the scale's inverse (a sphere flattened into a
    # card keeps a round one's falloff): the normals that come out so once the piece is scaled
    squared = node("ShaderNodeVectorMath", operation='MULTIPLY')
    links.new(inputs.outputs["Piece Scale"], squared.inputs[0])
    links.new(inputs.outputs["Piece Scale"], squared.inputs[1])
    bent = node("ShaderNodeVectorMath", operation='MULTIPLY')
    links.new(node("GeometryNodeInputNormal").outputs[0], bent.inputs[0])
    links.new(squared.outputs[0], bent.inputs[1])
    unit = node("ShaderNodeVectorMath", operation='NORMALIZE')
    links.new(bent.outputs[0], unit.inputs[0])
    shaded = node("GeometryNodeSetMeshNormal", mode='FREE', domain='CORNER')
    links.new(placed.outputs[0], shaded.inputs["Mesh"])
    links.new(unit.outputs[0], shaded.inputs["Custom Normal"])

    # how the piece is turned on its particle
    camera = node("GeometryNodeObjectInfo", transform_space='RELATIVE')
    links.new(node("GeometryNodeInputActiveCamera").outputs[0], camera.inputs["Object"])
    toward = node("ShaderNodeVectorMath", operation='SUBTRACT')
    links.new(camera.outputs["Location"], toward.inputs[0])
    links.new(node("GeometryNodeInputPosition").outputs[0], toward.inputs[1])
    velocity = attribute("mp_velocity", 'FLOAT_VECTOR')
    own = attribute("mp_rotation", 'QUATERNION')
    spin = node("FunctionNodeAxisAngleToRotation")
    spin.inputs["Axis"].default_value = (0.0, 0.0, 1.0)
    links.new(attribute("mp_spin", 'FLOAT'), spin.inputs["Angle"])

    def spun(rotation):
        n = node("FunctionNodeRotateRotation", rotation_space='LOCAL')
        links.new(rotation, n.inputs[0])
        links.new(spin.outputs[0], n.inputs[1])
        return n.outputs[0]

    def aligned(axis, to, rotation=None, pivot='AUTO'):
        n = node("FunctionNodeAlignRotationToVector", axis=axis, pivot_axis=pivot)
        if rotation is not None:
            links.new(rotation, n.inputs["Rotation"])
        if hasattr(to, "is_linked"):
            links.new(to, n.inputs["Vector"])
        else:
            n.inputs["Vector"].default_value = to
        return n.outputs[0]

    def mesh_facing(direction):
        # the mesh's X axis along the direction, its Z as far up as that leaves, then the particle's own rotation
        n = node("FunctionNodeRotateRotation", rotation_space='LOCAL')
        links.new(aligned('Z', (0.0, 0.0, 1.0), aligned('X', direction), 'X'), n.inputs[0])
        links.new(own, n.inputs[1])
        return n.outputs[0]

    turns = [
        own,
        spun(camera.outputs["Rotation"]),                                   # a sprite: the camera's plane, spun
        aligned('Z', toward.outputs[0], aligned('Y', velocity), 'Y'),       # its length along the velocity, its face to the camera
        spun(aligned('Z', attribute("mp_facing", 'FLOAT_VECTOR'))),         # its face to the particle's own direction
        mesh_facing(velocity),
        mesh_facing(toward.outputs[0]),
    ]
    rotation = node("GeometryNodeIndexSwitch", data_type='ROTATION')
    while len(rotation.index_switch_items) < len(turns):
        rotation.index_switch_items.new()
    links.new(inputs.outputs["Turn"], rotation.inputs["Index"])
    for i, turn in enumerate(turns):
        links.new(turn, rotation.inputs[i + 1])

    instances = node("GeometryNodeInstanceOnPoints")
    links.new(current.outputs[0], instances.inputs["Points"])
    links.new(shaded.outputs[0], instances.inputs["Instance"])
    links.new(rotation.outputs[0], instances.inputs["Rotation"])
    links.new(attribute("mp_scale", 'FLOAT_VECTOR'), instances.inputs["Scale"])
    outputs.location = (column[0], 0)
    links.new(instances.outputs[0], outputs.inputs[0])
    return g


def ribbons():
    """The node group that plays a ribbon's points mesh: the current frame's points strung into
    ribbons (one per ribbon ID, in link order), as wide as each particle says, turned to the camera."""
    g = bpy.data.node_groups.get(RIBBONS)
    if g is not None and g.get("mp_version") == GROUP_VERSION:
        return g
    g = bpy.data.node_groups.new(RIBBONS, 'GeometryNodeTree')
    g["mp_version"] = GROUP_VERSION
    g.is_modifier = True
    face = g.interface
    face.new_socket(name="Geometry", in_out='INPUT', socket_type='NodeSocketGeometry')
    face.new_socket(name="Geometry", in_out='OUTPUT', socket_type='NodeSocketGeometry')
    face.new_socket(name="Material", in_out='INPUT', socket_type='NodeSocketMaterial')
    face.new_socket(name="Start Frame", in_out='INPUT', socket_type='NodeSocketInt', description="The scene frame the effect starts on").default_value = 1
    face.new_socket(name="Frames", in_out='INPUT', socket_type='NodeSocketInt', description="How many frames were replayed").default_value = 1
    face.new_socket(name="Loop", in_out='INPUT', socket_type='NodeSocketBool', description="Start over after the last replayed frame")
    face.new_socket(name="Face Camera", in_out='INPUT', socket_type='NodeSocketBool', description="The ribbon's face turned to the scene camera (else to each particle's own facing)").default_value = True

    nodes, links = g.nodes, g.links
    column = [0]

    def node(kind, **settings):
        n = nodes.new(kind)
        n.location = (column[0], 0)
        column[0] += 190
        for k, v in settings.items():
            setattr(n, k, v)
        return n

    def attribute(name, kind):
        n = node("GeometryNodeInputNamedAttribute", data_type=kind)
        n.inputs["Name"].default_value = name
        return n.outputs[0]

    def math_(op, a, b):
        n = node("ShaderNodeMath", operation=op)
        links.new(a, n.inputs[0])
        links.new(b, n.inputs[1])
        return n.outputs[0]

    def vector(op, a, b=None):
        n = node("ShaderNodeVectorMath", operation=op)
        links.new(a, n.inputs[0])
        if b is not None:
            links.new(b, n.inputs[1])
        return n.outputs[0]

    inputs, outputs = node("NodeGroupInput"), nodes.new("NodeGroupOutput")
    time = node("GeometryNodeInputSceneTime")
    index = math_('SUBTRACT', time.outputs["Frame"], inputs.outputs["Start Frame"])
    looped = math_('FLOORED_MODULO', index, inputs.outputs["Frames"])
    which = node("GeometryNodeSwitch", input_type='FLOAT')
    links.new(inputs.outputs["Loop"], which.inputs["Switch"])
    links.new(index, which.inputs["False"])
    links.new(looped, which.inputs["True"])
    other = node("FunctionNodeCompare", data_type='FLOAT', operation='NOT_EQUAL')
    links.new(attribute("mp_frame", 'INT'), other.inputs[0])
    links.new(which.outputs[0], other.inputs[1])
    other.inputs["Epsilon"].default_value = 0.5
    current = node("GeometryNodeDeleteGeometry", domain='POINT')
    links.new(inputs.outputs["Geometry"], current.inputs["Geometry"])
    links.new(other.outputs[0], current.inputs["Selection"])
    cloud = node("GeometryNodeMeshToPoints")
    links.new(current.outputs[0], cloud.inputs[0])

    strung = node("GeometryNodePointsToCurves")
    links.new(cloud.outputs[0], strung.inputs["Points"])
    links.new(attribute("mp_ribbon", 'INT'), strung.inputs["Curve Group ID"])
    links.new(attribute("mp_order", 'FLOAT'), strung.inputs["Weight"])
    smooth = node("GeometryNodeCurveSplineType", spline_type='CATMULL_ROM')
    links.new(strung.outputs[0], smooth.inputs["Curve"])
    fine = node("GeometryNodeSetSplineResolution")
    links.new(smooth.outputs[0], fine.inputs[0])
    fine.inputs["Resolution"].default_value = 4
    wide = node("GeometryNodeSetCurveRadius")
    links.new(fine.outputs[0], wide.inputs["Curve"])
    links.new(attribute("mp_width", 'FLOAT'), wide.inputs["Radius"])

    # the ribbon's width runs across what it faces: the camera, or the particles' own facing
    camera = node("GeometryNodeObjectInfo", transform_space='RELATIVE')
    links.new(node("GeometryNodeInputActiveCamera").outputs[0], camera.inputs["Object"])
    toward = vector('SUBTRACT', camera.outputs["Location"], node("GeometryNodeInputPosition").outputs[0])
    facing = node("GeometryNodeSwitch", input_type='VECTOR')
    links.new(inputs.outputs["Face Camera"], facing.inputs["Switch"])
    links.new(attribute("mp_facing", 'FLOAT_VECTOR'), facing.inputs["False"])
    links.new(toward, facing.inputs["True"])
    side = vector('NORMALIZE', vector('CROSS_PRODUCT', node("GeometryNodeInputTangent").outputs[0], facing.outputs[0]))
    turned = node("GeometryNodeSetCurveNormal")
    links.new(wide.outputs[0], turned.inputs["Curve"])
    if "Mode" in turned.inputs:
        turned.inputs["Mode"].default_value = 'Free'
    else:
        turned.mode = 'FREE'
    links.new(side, turned.inputs["Normal"])

    # UVs: U along the ribbon, V across it
    along = node("GeometryNodeCaptureAttribute", domain='POINT')
    along.capture_items.new('FLOAT', "U")
    links.new(turned.outputs[0], along.inputs[0])
    links.new(node("GeometryNodeSplineParameter").outputs["Factor"], along.inputs[1])
    profile = node("GeometryNodeCurvePrimitiveLine")
    profile.inputs["Start"].default_value = (-0.5, 0.0, 0.0)
    profile.inputs["End"].default_value = (0.5, 0.0, 0.0)
    across = node("GeometryNodeCaptureAttribute", domain='POINT')
    across.capture_items.new('FLOAT', "V")
    links.new(profile.outputs[0], across.inputs[0])
    links.new(node("GeometryNodeSplineParameter").outputs["Factor"], across.inputs[1])
    ribbon = node("GeometryNodeCurveToMesh")
    links.new(along.outputs[0], ribbon.inputs["Curve"])
    links.new(across.outputs[0], ribbon.inputs["Profile Curve"])
    uv = node("ShaderNodeCombineXYZ")
    links.new(along.outputs[1], uv.inputs[0])
    links.new(across.outputs[1], uv.inputs[1])
    mapped = node("GeometryNodeStoreNamedAttribute", data_type='FLOAT2', domain='CORNER')
    mapped.inputs["Name"].default_value = "UV0"
    links.new(ribbon.outputs[0], mapped.inputs[0])
    links.new(uv.outputs[0], mapped.inputs["Value"])
    material = node("GeometryNodeSetMaterial")
    links.new(mapped.outputs[0], material.inputs[0])
    links.new(inputs.outputs["Material"], material.inputs["Material"])
    outputs.location = (column[0], 0)
    links.new(material.outputs[0], outputs.inputs[0])
    return g


def _set(modifier, tree, name, value):
    identifier = next(i.identifier for i in tree.interface.items_tree if i.item_type == 'SOCKET' and i.in_out == 'INPUT' and i.name == name)
    getattr(modifier.properties.inputs, identifier).value = value


def _camera(scene, root, scale):
    """The scene camera as a script sees it: its position, forward, up and right in the effect's
    own space, UE's axes and units. None without a camera."""
    if scene.camera is None:
        return None
    bpy.context.view_layer.update()
    m = root.matrix_world.inverted() @ scene.camera.matrix_world
    flip = lambda v: (v.x, -v.y, v.z)
    position = tuple(c / scale for c in flip(m.translation))
    return (position, flip(-m.col[2].xyz.normalized()), flip(m.col[1].xyz.normalized()), flip(m.col[0].xyz.normalized()))


def play(root, exports, fields, scale):
    """Replay the system under the root (its emitters' empties, their pieces) and make its CPU
    emitters' pieces play. Yields what to tell the user."""
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    system, tracks = replay(exports, fields, fps, max(1, scene.frame_end - scene.frame_start + 1), _camera(scene, root, scale))
    emitters = {e.name: e for e in system.emitters}
    played, most, frames = [], 0, 0
    for node in root.children:
        emitter = emitters.get(node.get(effects.KEY_EMITTER))
        if emitter is None or not tracks.get(emitter.name):
            continue
        track = Track(emitter, tracks[emitter.name])
        if not track.total:
            continue
        drawn = 0
        for piece in list(node.children):
            kind = piece.get(effects.KEY)
            renderer = next((e["props"] for e in exports if e["name"] == piece.get(effects.KEY_RENDERER) and e["outer"] == emitter.export["name"]), None)
            if kind not in ("Sprite", "Mesh", "Ribbon") or renderer is None or piece.type != 'MESH':
                continue
            keep = None
            if kind == "Mesh" and track.has(bound(renderer, "MeshIndexBinding", "MeshIndex")):
                keep = track.get(bound(renderer, "MeshIndexBinding", "MeshIndex"), (0,))[:, 0] == int(piece.get("mp_mesh_index", 0))
            elif kind == "Mesh" and int(piece.get("mp_mesh_index", 0)) > 0:
                continue        # without a mesh index every particle draws the first mesh
            tree = ribbons() if kind == "Ribbon" else group()
            data = points(piece.name + " particles", track, renderer, kind, scale, keep)
            obj = bpy.data.objects.new(piece.name + " particles", data)
            obj.parent = node
            obj[effects.KEY] = "Particles"
            obj.visible_shadow = False
            for collection in piece.users_collection:
                collection.objects.link(obj)
            modifier = obj.modifiers.new("Particles", 'NODES')
            modifier.node_group = tree
            _set(modifier, tree, "Start Frame", scene.frame_start)
            _set(modifier, tree, "Frames", len(track.frames))
            if kind == "Ribbon":
                _set(modifier, tree, "Material", piece.material_slots[0].material if piece.material_slots else None)
                _set(modifier, tree, "Face Camera", "Custom" not in str(renderer.get("FacingMode")))
                piece.hide_render = True
                piece.hide_viewport = True
                drawn += 1
                continue
            _set(modifier, tree, "Piece", piece)
            if kind == "Sprite":
                turn = TURN_FACING if "CustomFacing" in str(renderer.get("FacingMode")) else \
                    TURN_CAMERA_VELOCITY if "VelocityAligned" in str(renderer.get("Alignment")) else TURN_CAMERA
                for constraint in list(piece.constraints):      # the still piece's turn to the camera: the modifier's now
                    piece.constraints.remove(constraint)
            else:
                facing = str(renderer.get("FacingMode"))
                turn = TURN_MESH_VELOCITY if "Velocity" in facing else TURN_MESH_CAMERA if "Camera" in facing else TURN_OWN
                _set(modifier, tree, "Piece Rotation", piece.rotation_euler)
                _set(modifier, tree, "Piece Scale", piece.scale)
            _set(modifier, tree, "Turn", turn)
            # the piece itself: what the particles draw, out of sight
            piece.hide_render = True
            piece.hide_viewport = True
            drawn += 1
        if drawn:
            node.location = (0.0, 0.0, 0.0)     # played where the effect is, not in the row of pieces
            played.append(emitter.name)
            most = max(most, int(track.counts.max()))
            frames = max(frames, len(track.frames))
    if played:
        # the emitters left as pieces: in a row beside the effect, 2 m apart
        left = [node for node in root.children if node.get(effects.KEY_EMITTER) and node.get(effects.KEY_EMITTER) not in played]
        for at, node in enumerate(left):
            node.location = (0.0, -200.0 * (at + 1) * scale, 0.0)
        yield "%s: %s played over %d frames from frame %d (%d particles at most)" % (
            root.name, ", ".join(played), frames, scene.frame_start, most)
    approximate = [name for name in played if name in system.approximate]
    if approximate:
        yield "%s: %s: stateless emitters, played from their settings (the engine's random draws apart)" % (root.name, ", ".join(approximate))
    idle = [e.name for e in system.emitters if e.name not in played and not sum(f[0].shape[1] for f in tracks.get(e.name) or [])]
    if idle:
        yield "%s: %s spawned nothing in the replay (waiting on something the game sets, or on an emitter left out): left as pieces" % (root.name, ", ".join(idle))
    if system.unattached:
        yield "%s: reads a character's bones or sockets: there is none here, each sits at the effect's origin" % root.name
    left = ["%s (%s)" % s for s in system.skipped]
    if left:
        yield "%s: left as pieces: %s" % (root.name, ", ".join(left))
