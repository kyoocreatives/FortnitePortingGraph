"""Material Porter fork: a control rig for a vehicle's armature (a Valet car, a Rocket Racing car, a
dirt bike, a tank...). Fortnite's vehicles share a layout - root > frame > body, and each wheel
under a differential: axle_pivot > steering_knuckle > wheel_steering > wheel_disc > tire (a tank's
road_wheel > rot_road_wheel) - which the rig reads by name, with fallbacks on position:

- CR_Main places and turns the vehicle; CR_Drive (under it) is moved forward along its own axis to
  drive: the vehicle follows it (the root's Child Of), and every wheel spins by the distance over
  its radius (a Transformation constraint: driving a metre turns a wheel 1/r radians);
- CR_Steer (on the front axle, under CR_Drive) turns about its own axis: the front wheels turn with
  it, the cockpit's steering wheel three times as much;
- the body, the frame and the vehicle's parts (a turret, a gun, doors, a hatch) are FK on their own
  bones, with shapes. The original bones keep their names, rest pose and hierarchy."""

import re
from math import pi

import bpy
from mathutils import Matrix, Vector

KEY = "is_vehicle_rig"
PREFIX = "CR_"
SPIN = re.compile(r"^(tire|wheel_disc|rot_road_wheel|rot_drive_sprocket|rot_.*wheel)", re.IGNORECASE)
WHEELISH = re.compile(r"wheel|tire|tyre", re.IGNORECASE)
NOT_A_WHEEL = re.compile(r"steering|tilted|well|socket|fx_|spreader|hand|brake", re.IGNORECASE)
STEER = re.compile(r"^wheel_steering", re.IGNORECASE)
COCKPIT = re.compile(r"^(steering_wheel|handlebars?)$", re.IGNORECASE)
FRONT = re.compile(r"(^|_)(fr|fl|front|f)(_|$)", re.IGNORECASE)
BACK = re.compile(r"(^|_)(bk|br|bl|back|rear|b)(_|$)", re.IGNORECASE)
HELPER = re.compile(r"fx_|socket|physlight|passenger|driver|exit|choice|pushforce|^center$|watertest|lookahead|"
                    r"muzzle|attach|_hand_|^frontwheels$|^rearwheels$|thrust|lensflare|taillight", re.IGNORECASE)
STEER_LIMIT = pi / 3        # the steer control's full turn: the front wheels' (60 degrees)
COCKPIT_RATIO = 3.0         # the steering wheel turns this much more than the wheels


def _axis(matrix, direction):
    """The bone's local axis (X, Y or Z) closest to a direction, and its sign."""
    best = max(range(3), key=lambda i: abs(matrix.col[i].to_3d().normalized().dot(direction)))
    return "XYZ"[best], 1.0 if matrix.col[best].to_3d().dot(direction) >= 0 else -1.0


class Survey:
    """What a vehicle's skeleton is made of (edit bones, armature space)."""

    def __init__(self, edit_bones):
        self.bones = {b.name: b for b in edit_bones}
        tops = [b for b in edit_bones if b.parent is None]
        self.root = next((b.name for b in tops if b.name.lower() == "root"), tops[0].name if tops else None)
        self.ground = self.bones[self.root].head.z if self.root else 0.0
        named = [n for n in self.bones if SPIN.search(n)]
        if not named:
            named = [n for n in self.bones if WHEELISH.search(n) and not NOT_A_WHEEL.search(n)]
        # one spinning bone a wheel: the topmost (a disc and its tire turn as one)
        self.wheels = [n for n in named if not any(p.name in named for p in self.bones[n].parent_recursive)]
        front = [n for n in self.wheels if FRONT.search(n)]
        back = [n for n in self.wheels if BACK.search(n)]
        centre = lambda names: sum((self.bones[n].head for n in names), Vector()) / len(names)
        if front and back:
            forward = centre(front) - centre(back)
        else:
            forward = Vector((1.0, 0.0, 0.0))
        forward.z = 0.0
        self.forward = forward.normalized() if forward.length > 1e-6 else Vector((1.0, 0.0, 0.0))
        self.left = Vector((0.0, 0.0, 1.0)).cross(self.forward)
        heads = [b.head for b in edit_bones] + [b.tail for b in edit_bones]
        along = [h.dot(self.forward) for h in heads] or [0.0]
        self.length = max(max(along) - min(along), 0.5)
        middle = sum(along) / len(along)
        steering = [n for n in self.bones if STEER.search(n)]
        self.steering = [n for n in steering if FRONT.search(n) or (not BACK.search(n) and self.bones[n].head.dot(self.forward) > middle)]
        self.cockpit = [n for n in self.bones if COCKPIT.search(n)]
        self.front_axle = centre(self.steering) if self.steering else (centre(front) if front else None)
        self.wheel_chains = set()
        for n in self.wheels + self.steering:
            at = self.bones[n]
            self.wheel_chains.add(n)
            self.wheel_chains.update(c.name for c in at.children_recursive)
            for p in at.parent_recursive:
                if "differential" in p.name.lower() or p.name in (self.root, "frame", "body"):
                    break
                self.wheel_chains.add(p.name)

    def radius(self, name):
        height = self.bones[name].head.z - self.ground
        return height if height > 0.05 else 0.35


def _transform(owner, target, subtarget, source, to_axis, scale, map_from):
    """A Transformation constraint: the target's local `source` (location along Y, or rotation about
    it) turns the owner about its local `to_axis`, times `scale` (extrapolated past the range)."""
    con = owner.constraints.new('TRANSFORM')
    con.name = "CR Drive" if map_from == 'LOCATION' else "CR Steer"
    con.target, con.subtarget = target, subtarget
    con.target_space = con.owner_space = 'LOCAL'
    con.map_from, con.map_to = map_from, 'ROTATION'
    con.mix_mode_rot = 'ADD'
    con.use_motion_extrapolate = True
    if map_from == 'LOCATION':
        setattr(con, "from_min_" + source.lower(), -1.0)
        setattr(con, "from_max_" + source.lower(), 1.0)
    else:
        setattr(con, "from_min_" + source.lower() + "_rot", -1.0)
        setattr(con, "from_max_" + source.lower() + "_rot", 1.0)
    for axis in "xyz":
        setattr(con, "map_to_%s_from" % axis, source if axis == to_axis.lower() else ("X" if source != "X" else "Z"))
        setattr(con, "to_min_%s_rot" % axis, -scale if axis == to_axis.lower() else 0.0)
        setattr(con, "to_max_%s_rot" % axis, scale if axis == to_axis.lower() else 0.0)
    return con


def _driven(obj, constraint, prop):
    driver = constraint.driver_add("influence").driver
    driver.type = 'SCRIPTED'
    var = driver.variables.new()
    var.name, var.type = "on", 'SINGLE_PROP'
    var.targets[0].id = obj
    var.targets[0].data_path = '["%s"]' % prop
    driver.expression = "on"


def _shape(pose_bone, shape, palette, size):
    pose_bone.custom_shape = bpy.data.objects.get(shape)
    pose_bone.color.palette = palette
    pose_bone.use_custom_shape_bone_size = False
    pose_bone.custom_shape_scale_xyz = (size, size, size)
    pose_bone.custom_shape_wire_width = 2.0


def create(obj):
    """Rig the armature object. Returns what it found, in a line."""
    from ...utils import ensure_blend_data
    armature = obj.data
    if armature.get(KEY):
        return "%s: already has a vehicle rig" % obj.name
    ensure_blend_data()             # the control shapes (CTRL_Root, CTRL_Box...)
    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        o.select_set(False)
    view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    edit = armature.edit_bones
    survey = Survey(edit)
    if survey.root is None:
        bpy.ops.object.mode_set(mode='OBJECT')
        return "%s: no bones" % obj.name

    # the controls: on the ground under the vehicle, pointing forward (their Y), Z up
    base = survey.bones[survey.root].head.copy()
    main = edit.new(PREFIX + "Main")
    main.head, main.tail = base, base + survey.forward * survey.length * 0.6
    main.roll = 0.0
    drive = edit.new(PREFIX + "Drive")
    drive.head, drive.tail = base, base + survey.forward * survey.length * 0.4
    drive.roll, drive.parent = 0.0, main
    for bone in (main, drive):
        bone.use_deform = False
    steer_name = None
    if survey.steering and survey.front_axle is not None:
        steer = edit.new(PREFIX + "Steer")
        at = Vector((survey.front_axle.x, survey.front_axle.y, base.z)) + survey.forward * survey.length * 0.15
        steer.head, steer.tail = at, at + Vector((0.0, 0.0, survey.length * 0.15))
        steer.roll, steer.parent, steer.use_deform = 0.0, drive, False
        steer_name = steer.name
    # each spinning bone's axle (its local axis across the vehicle) and each steering bone's upright one
    spins = {n: _axis(edit[n].matrix, survey.left) for n in survey.wheels}
    turns = {n: _axis(edit[n].matrix, Vector((0.0, 0.0, 1.0))) for n in survey.steering}
    cockpits = {n: _axis(edit[n].matrix, -survey.forward) for n in survey.cockpit}
    radii = {n: survey.radius(n) for n in survey.wheels}
    bpy.ops.object.mode_set(mode='POSE')
    pose = obj.pose.bones
    view_layer.update()

    # the vehicle follows CR_Drive (its root, from where it is at rest)
    follow = pose[survey.root].constraints.new('CHILD_OF')
    follow.name = "CR Follow"
    follow.target, follow.subtarget = obj, PREFIX + "Drive"
    follow.inverse_matrix = (obj.matrix_world @ pose[PREFIX + "Drive"].matrix).inverted()

    obj["auto_wheels"] = 1.0
    obj.id_properties_ui("auto_wheels").update(min=0.0, max=1.0, description="The wheels spin as CR_Drive moves forward")
    obj["auto_steer"] = 1.0
    obj.id_properties_ui("auto_steer").update(min=0.0, max=1.0, description="The front wheels and the steering wheel turn with CR_Steer")
    for name, (axis, sign) in spins.items():
        # driving forward a metre turns the wheel 1/r radians about its axle (forward roll: about +left)
        con = _transform(pose[name], obj, PREFIX + "Drive", "Y", axis, sign / radii[name], 'LOCATION')
        _driven(obj, con, "auto_wheels")
    if steer_name:
        for name, (axis, sign) in turns.items():
            con = _transform(pose[name], obj, steer_name, "Y", axis, sign, 'ROTATION')
            _driven(obj, con, "auto_steer")
        for name, (axis, sign) in cockpits.items():
            con = _transform(pose[name], obj, steer_name, "Y", axis, sign * COCKPIT_RATIO, 'ROTATION')
            _driven(obj, con, "auto_steer")
        # the steer control turns within the wheels' lock
        limit = pose[steer_name].constraints.new('LIMIT_ROTATION')
        limit.name = "CR Steer Lock"
        limit.use_limit_y, limit.min_y, limit.max_y = True, -STEER_LIMIT, STEER_LIMIT
        limit.owner_space = 'LOCAL'
        pose[steer_name].rotation_mode = 'YXZ'
        pose[steer_name].lock_rotation = (True, False, True)
        pose[steer_name].lock_location = (True, True, True)
    pose[PREFIX + "Drive"].lock_location = (True, False, True)      # it drives along its own axis

    # what shows: the controls, the body and frame, the vehicle's parts; the wheels' bones and the
    # game's helpers (sockets, effect points, seats) don't
    ours = ("Vehicle Controls", "Vehicle Parts", "Vehicle Wheels", "Vehicle Other")
    for collection in armature.collections:
        if collection.name not in ours:
            collection.is_visible = False
    collections = {}
    for name, visible in zip(ours, (True, True, False, False)):
        collections[name] = armature.collections.get(name) or armature.collections.new(name)
        collections[name].is_visible = visible
    size = survey.length
    _shape(pose[PREFIX + "Main"], "CTRL_Root", "THEME09", size * 0.7)
    _shape(pose[PREFIX + "Drive"], "CTRL_Box", "THEME04", size * 0.25)
    collections["Vehicle Controls"].assign(armature.bones[PREFIX + "Main"])
    collections["Vehicle Controls"].assign(armature.bones[PREFIX + "Drive"])
    if steer_name:
        _shape(pose[steer_name], "CTRL_Pole", "THEME01", size * 0.08)
        collections["Vehicle Controls"].assign(armature.bones[steer_name])
    for name in armature.bones.keys():
        if name.startswith(PREFIX):
            continue
        bone = armature.bones[name]
        if name in survey.wheel_chains:
            collections["Vehicle Wheels"].assign(bone)
        elif HELPER.search(name) or name == survey.root:
            collections["Vehicle Other"].assign(bone)
        else:
            collections["Vehicle Parts"].assign(bone)
            big = name.lower() in ("body", "frame")
            _shape(pose[name], "CTRL_Box", "THEME09" if big else "THEME02", size * (0.45 if big else 0.06))
    armature[KEY] = True
    bpy.ops.object.mode_set(mode='OBJECT')
    attached = _attach(obj, survey, radii)
    return "%s: vehicle rig (%d wheels, %d steering, %s%s)" % (
        obj.name, len(survey.wheels), len(survey.steering), "a steering wheel" if survey.cockpit else "no steering wheel",
        ", %d wheel object(s) put on their hubs" % attached if attached else "")


def _attach(obj, survey, radii):
    """What sits on the vehicle as an object of its own (a Rocket Racing car's wheels: an armature each
    at a hub; a part), parented to the armature object - it wouldn't follow the drive: put on a bone,
    where it is. One at a hub goes on that wheel's spinning bone, anything else on the body."""
    pose = obj.pose.bones
    body = next((n for n in ("body", "frame") if n in pose), survey.root)
    hubs = [(n, (obj.matrix_world @ pose[n].head)) for n in survey.wheels]
    put = 0
    bpy.context.view_layer.update()
    for child in list(obj.children):
        if child.parent_type != 'OBJECT' or any(m.type == 'ARMATURE' and m.object == obj for m in child.modifiers):
            continue
        where = child.matrix_world.translation
        near = min(hubs, key=lambda h: (h[1] - where).length, default=None)
        bone = near[0] if near is not None and (near[1] - where).length < 0.5 * radii[near[0]] else body
        world = child.matrix_world.copy()
        child.parent_type, child.parent_bone = 'BONE', bone
        tail = obj.matrix_world @ pose[bone].matrix @ Matrix.Translation((0.0, obj.data.bones[bone].length, 0.0))
        child.matrix_parent_inverse = tail.inverted()
        child.matrix_world = world
        put += bone != body
    return put
