"""Material Porter fork: a control rig for a creature's armature (a wolf, a chicken, a raptor, a LEGO
cow...), as Tasty's is for the player's. Creatures' skeletons have no names in common (Battle
Royale's modular ones - QuadSpine_A_Pelvis_C, PawedLeg_A_Thigh_L - LEGO Fortnite's simple ones -
pelvis, legB_01_l), so the rig reads each skeleton's tree: the spine from the pelvis to the head,
the limbs hanging off it (a side's bones under a centre bone), which of them reach the ground
(legs), the tail and the face's bones. Legs get IK (a foot control on the ground, a knee pole);
everything else is FK on its own bones, with shapes. The original bones keep their names, rest
pose and hierarchy (an animation still plays on them: turn the legs' IK off first)."""

import re

import bpy
from mathutils import Vector

KEY = "is_creature_rig"
PREFIX = "CR_"          # the rig's own bones: CR_IK_<foot>, CR_Pole_<limb>

SIDE = re.compile(r"(^|[_.\-\s])(l|left)([_.\-\s]|$)|(^|[_.\-\s])(r|right)([_.\-\s]|$)", re.IGNORECASE)
FOOT = re.compile(r"^(foot|ankle|wrist|paw|hoof)[a-z]?$")      # a name's word (footA, Ankle - not PawedArm)
HEAD = re.compile(r"(^|_)head(_c|_jnt)?$", re.IGNORECASE)
PELVIS = re.compile(r"pelvis|hips", re.IGNORECASE)
TAIL = re.compile(r"tail", re.IGNORECASE)
SHOULDER_BLADE = re.compile(r"clavicle|scapula", re.IGNORECASE)
HELPER = re.compile(r"^(ik_|fx_|vb |.*socket)|attach|null$|_effect$", re.IGNORECASE)


def side_of(name):
    """"L", "R" or "C" from a bone's name (thigh_l, PawedLeg_A_Thigh_L, L_Wing...)."""
    m = SIDE.search(name)
    if m is None:
        return "C"
    return "L" if m.group(2) else "R"


class Limb:
    def __init__(self, bones, kind):
        self.bones = bones          # names, from the top down
        self.kind = kind            # "leg", "arm", "wing", "face"
        self.chain = []             # a leg's IK chain (names, top down)
        self.foot = None            # a leg's foot (the IK target's bone)

    @property
    def name(self):
        return self.chain[0] if self.chain else self.bones[0]


class Survey:
    """What a skeleton is made of, read from its edit bones (armature space)."""

    def __init__(self, edit_bones):
        self.bones = {b.name: b for b in edit_bones}
        usable = [b for b in edit_bones if not HELPER.search(b.name)]
        zs = [p.z for b in usable for p in (b.head, b.tail)] or [0.0]
        self.ground, self.height = min(zs), max(max(zs) - min(zs), 1e-6)
        tops = [b for b in edit_bones if b.parent is None]
        self.root = next((b.name for b in tops if b.name.lower() == "root"), tops[0].name if tops else None)
        self.pelvis = self._pelvis(edit_bones)
        self.spine = self._spine()
        self.head = next((n for n in self.spine if HEAD.search(n)), None)
        self.tail = self._tail()
        self.limbs = self._limbs(edit_bones)

    def _descendants(self, name):
        return len(self.bones[name].children_recursive)

    def _pelvis(self, edit_bones):
        named = [b.name for b in edit_bones if PELVIS.search(b.name) and side_of(b.name) == "C" and not HELPER.search(b.name)]
        if named:
            return min(named, key=lambda n: len(self.bones[n].parent_recursive))
        # the root's centre child with the most bones under it
        root = self.bones.get(self.root)
        children = [c for c in (root.children if root else []) if not HELPER.search(c.name)]
        return max(children, key=lambda c: len(c.children_recursive)).name if children else self.root

    def _spine(self):
        """From the pelvis to the head: each step the centre child with the most bones under it (not the tail)."""
        path, at = [], self.pelvis
        while at is not None:
            path.append(at)
            if HEAD.search(at):
                break
            centre = [c for c in self.bones[at].children if side_of(c.name) == "C" and not TAIL.search(c.name)
                      and not HELPER.search(c.name) and c.children_recursive]
            at = max(centre, key=lambda c: len(c.children_recursive)).name if centre else None
        return path

    def _tail(self):
        starts = [b for b in self.bones.values() if TAIL.search(b.name) and side_of(b.name) == "C"
                  and (b.parent is None or not TAIL.search(b.parent.name))]
        if not starts:
            return []
        start = min(starts, key=lambda b: len(b.parent_recursive))
        chain, at = [], start
        while at is not None:
            chain.append(at.name)
            at = max(at.children, key=lambda c: len(c.children_recursive)) if at.children else None
        return chain

    def _limbs(self, edit_bones):
        head_subtree = set(c.name for c in self.bones[self.head].children_recursive) | {self.head} if self.head else set()
        limbs = []
        for b in edit_bones:
            if side_of(b.name) == "C" or HELPER.search(b.name) or b.parent is None or side_of(b.parent.name) != "C":
                continue
            # down the limb: each step the child with the most bones under it
            chain, at = [], b
            while at is not None:
                chain.append(at.name)
                kids = [c for c in at.children if not HELPER.search(c.name)]
                at = max(kids, key=lambda c: len(c.children_recursive)) if kids else None
            if b.name in head_subtree or b.parent.name in head_subtree:
                kind = "face"
            elif "wing" in b.name.lower():
                kind = "wing"
            else:
                low = min(min(self.bones[n].head.z, self.bones[n].tail.z) for n in chain)
                kind = "leg" if low < self.ground + 0.2 * self.height and len(chain) >= 3 else "arm"
            limb = Limb(chain, kind)
            if kind == "leg":
                self._leg(limb)
            limbs.append(limb)
        return limbs

    def _leg(self, limb):
        """A leg's IK: from its first bone (past a clavicle or scapula) to the one above its foot."""
        names = limb.bones
        start = 1 if SHOULDER_BLADE.search(names[0]) and len(names) > 3 else 0
        foot = next((i for i in range(start + 1, len(names)) if any(FOOT.match(w.lower()) for w in re.split(r"[_\d]+", names[i]))), None)
        if foot is None:
            foot = len(names) - 2 if len(names) - start >= 3 else None     # (the last is its toe)
        if foot is None or foot - start < 2:
            limb.kind = "arm"       # too short to bend: FK
            return
        limb.chain, limb.foot = names[start:foot], names[foot]


def _pole_angle(base, tip, pole):
    """The IK constraint's pole angle that keeps the chain's rest pose (base: its first edit bone,
    tip: where it ends - the foot's head)."""
    def signed(a, b, normal):
        angle = a.angle(b)
        return -angle if a.cross(b).angle(normal) < 1.0 else angle
    normal = (tip - base.head).cross(pole - base.head)
    axis = normal.cross(base.tail - base.head)
    return signed(base.x_axis, axis, base.tail - base.head)


def _collection(armature, name, visible=True):
    collection = armature.collections.get(name) or armature.collections.new(name)
    collection.is_visible = visible
    return collection


def _shape(pose_bone, shape, palette, scale=1.0, by_length=True, wire=2.0):
    pose_bone.custom_shape = bpy.data.objects.get(shape)
    pose_bone.color.palette = palette
    pose_bone.use_custom_shape_bone_size = by_length
    pose_bone.custom_shape_scale_xyz = (scale, scale, scale)
    pose_bone.custom_shape_wire_width = wire


def _palette(name, centre="THEME09"):
    side = side_of(name)
    return "THEME01" if side == "R" else "THEME04" if side == "L" else centre


def _driven(obj, constraint, prop):
    driver = constraint.driver_add("influence").driver
    driver.type = 'SCRIPTED'
    var = driver.variables.new()
    var.name, var.type = "on", 'SINGLE_PROP'
    var.targets[0].id = obj
    var.targets[0].data_path = '["%s"]' % prop
    driver.expression = "on"


def create(obj):
    """Rig the armature object. Returns what it found, in a line."""
    from ...utils import ensure_blend_data
    armature = obj.data
    if armature.get(KEY):
        return "%s: already has a creature rig" % obj.name
    ensure_blend_data()             # the control shapes (CTRL_Box, CTRL_Pole...)
    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        o.select_set(False)
    view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    edit = armature.edit_bones
    survey = Survey(edit)
    legs = [l for l in survey.limbs if l.kind == "leg"]

    # the legs' IK targets (a copy of the foot, under the root) and knee poles
    poles = {}
    for leg in legs:
        foot, base, tip = edit[leg.foot], edit[leg.chain[0]], edit[leg.chain[-1]]
        target = edit.new(PREFIX + "IK_" + leg.foot)
        target.head, target.tail, target.roll = foot.head.copy(), foot.tail.copy(), foot.roll
        target.parent = edit.get(survey.root)
        target.use_deform = False
        # the pole: out from the chain's bend, a limb's length away (in front of a knee, behind a hock)
        length = sum(edit[n].length for n in leg.chain)
        line = tip.tail - base.head
        middle = edit[leg.chain[len(leg.chain) // 2]].head if len(leg.chain) > 1 else base.tail
        along = base.head + line * ((middle - base.head).dot(line) / max(line.length_squared, 1e-9))
        bend = middle - along
        if bend.length < 1e-4 * length:
            bend = base.z_axis.copy()
        location = middle + bend.normalized() * length
        pole = edit.new(PREFIX + "Pole_" + leg.name)
        pole.head, pole.tail = location, location + Vector((0, 0, 0.1 * length))
        pole.parent = edit.get(survey.root)
        pole.use_deform = False
        poles[leg.name] = _pole_angle(base, foot.head, location)
    bpy.ops.object.mode_set(mode='POSE')

    ours = ("Creature Controls", "Creature Face", "Creature Other")
    for collection in armature.collections:
        if collection.name not in ours:
            collection.is_visible = False       # (the import's own: the rig's say what shows)
    controls = _collection(armature, "Creature Controls")
    face = _collection(armature, "Creature Face")
    others = _collection(armature, "Creature Other", visible=False)
    pose = obj.pose.bones

    def show(name, collection, shape, palette, factor=1.0):
        """A control's shape, sized by its bone (a bone of a centimetre - a LEGO pelvis - by the creature)."""
        if name not in pose:
            return
        bone = armature.bones[name]
        _shape(pose[name], shape, palette, max(bone.length, 0.08 * survey.height) * factor, by_length=False)
        collection.assign(bone)

    for name in armature.bones.keys():
        others.assign(armature.bones[name])
    if survey.root:
        show(survey.root, controls, "CTRL_Root", "THEME09")
        pose[survey.root].custom_shape_scale_xyz = (survey.height * 0.6,) * 3
    for name in survey.spine:
        show(name, controls, "CTRL_Spine", "THEME09", 1.2)
    if survey.pelvis in pose:
        show(survey.pelvis, controls, "CTRL_Box", "THEME09", 1.5)
    # the bones between the spine and a limb or the tail (a hips bone the rear legs hang from)
    spine = set(survey.spine)
    for root in [l.bones[0] for l in survey.limbs if l.kind != "face"] + survey.tail[:1]:
        at = armature.bones[root].parent
        while at is not None and at.name not in spine and side_of(at.name) == "C":
            show(at.name, controls, "CTRL_Box", "THEME09", 1.0)
            at = at.parent
    for name in survey.tail:
        show(name, controls, "CTRL_Box", "THEME09", 0.5)
    for limb in survey.limbs:
        target = face if limb.kind == "face" else controls
        for name in limb.bones:
            show(name, target, "CTRL_Box", _palette(name), 0.4 if limb.kind == "face" else 0.5)
    if survey.head:
        for child in armature.bones[survey.head].children_recursive:
            if child.name not in pose or HELPER.search(child.name) or any(child.name in l.bones for l in survey.limbs):
                continue
            show(child.name, face, "CTRL_Box", _palette(child.name, "THEME02"), 0.4)

    for leg in legs:
        prop = "ik_" + leg.name
        obj[prop] = 1.0
        obj.id_properties_ui(prop).update(min=0.0, max=1.0, description="IK on this leg (0: FK, as an animation plays it)")
        target_name, pole_name = PREFIX + "IK_" + leg.foot, PREFIX + "Pole_" + leg.name
        show(target_name, controls, "CTRL_Box", _palette(leg.foot), 1.2)
        show(pole_name, controls, "CTRL_Pole", _palette(leg.foot), 0.5)
        # on the foot, its head the chain's tip: a reoriented bone's tail needn't meet its child's head
        ik = pose[leg.foot].constraints.new('IK')
        ik.name = "CR IK"
        ik.use_tail = False
        ik.target, ik.subtarget = obj, target_name
        ik.pole_target, ik.pole_subtarget = obj, pole_name
        ik.pole_angle = poles[leg.name]
        ik.chain_count = len(leg.chain)     # (the foot itself isn't a segment: its head is the tip)
        _driven(obj, ik, prop)
        turn = pose[leg.foot].constraints.new('COPY_ROTATION')
        turn.name = "CR IK Foot"
        turn.target, turn.subtarget = obj, target_name
        _driven(obj, turn, prop)
    armature[KEY] = True
    bpy.ops.object.mode_set(mode='OBJECT')
    parts = ["spine %d" % len(survey.spine), "legs %d" % len(legs),
             "arms %d" % sum(1 for l in survey.limbs if l.kind == "arm"),
             "wings %d" % sum(1 for l in survey.limbs if l.kind == "wing"), "tail %d" % len(survey.tail)]
    return "%s: creature rig (%s)" % (obj.name, ", ".join(parts))
