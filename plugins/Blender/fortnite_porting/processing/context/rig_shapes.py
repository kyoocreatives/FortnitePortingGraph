"""Material Porter fork: control shapes the creature and vehicle rigs draw themselves (Tasty's CTRL_
shapes come from the add-on's data blend; these are built here, once a file). Each is a wire in its
XY plane, Z its up."""

from math import cos, radians, sin

import bpy
from mathutils import Vector


def _arrow():
    """An arrow from the origin to +Y 1, 0.4 wide at its head."""
    shaft, head, neck = 0.08, 0.2, 0.65
    points = [(-shaft, 0.0), (shaft, 0.0), (shaft, neck), (head, neck), (0.0, 1.0), (-head, neck), (-shaft, neck)]
    verts = [(x, y, 0.0) for x, y in points]
    return verts, [(i, (i + 1) % len(verts)) for i in range(len(verts))]


def _turn(degrees=55.0):
    """An arc of radius 1 about Z, `degrees` each side of +Y, an arrowhead at each end: turn it."""
    span, steps = radians(degrees), 24
    angles = [-span + 2.0 * span * i / steps for i in range(steps + 1)]
    verts = [(sin(a), cos(a), 0.0) for a in angles]
    edges = [(i, i + 1) for i in range(steps)]
    for end, sign in ((0, -1.0), (steps, 1.0)):
        a = sign * span
        tip = Vector((sin(a), cos(a), 0.0))
        along = Vector((cos(a), -sin(a), 0.0)) * sign       # on along the arc, out of its end
        for side in (-1.0, 1.0):
            verts.append(tuple(tip - along * 0.14 + tip * 0.07 * side))
            edges.append((end, len(verts) - 1))
    return verts, edges


def _foot():
    """A footprint about the origin: a rounded box 0.6 wide (X), 1 long (Y), its front a point."""
    verts, steps, r = [], 6, 0.2
    for cx, cy, start in ((0.1, 0.3, 0.0), (-0.1, 0.3, 90.0), (-0.1, -0.3, 180.0), (0.1, -0.3, 270.0)):
        for i in range(steps + 1):
            a = radians(start + 90.0 * i / steps)
            verts.append((cx + r * cos(a), cy + r * sin(a), 0.0))
    edges = [(i, (i + 1) % len(verts)) for i in range(len(verts))]
    verts += [(-0.12, 0.62, 0.0), (0.0, 0.72, 0.0), (0.12, 0.62, 0.0)]     # which way it faces
    n = len(verts)
    edges += [(n - 3, n - 2), (n - 2, n - 1)]
    return verts, edges


SHAPES = {"CR_Arrow": _arrow, "CR_Turn": _turn, "CR_Swing": lambda: _turn(18.0), "CR_Foot": _foot}


def ensure(name):
    """The shape object (built the first time)."""
    obj = bpy.data.objects.get(name)
    if obj is not None and obj.type == 'MESH':
        return obj
    verts, edges = SHAPES[name]()
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, edges, [])
    return bpy.data.objects.new(name, mesh)


def place(obj, pose_bone, at):
    """Move a control's shape to a point (armature space) off its bone."""
    bone = obj.data.bones[pose_bone.name]
    pose_bone.custom_shape_translation = bone.matrix_local.to_3x3().inverted() @ (at - bone.head_local)


def color(pose_bone, rgb):
    """A control's own colour (the theme's sets are dark as wires): `rgb` 0-1, lighter selected."""
    pose_bone.color.palette = 'CUSTOM'
    pose_bone.color.custom.normal = rgb
    pose_bone.color.custom.select = tuple(min(1.0, c * 0.5 + 0.5) for c in rgb)
    pose_bone.color.custom.active = (1.0, 1.0, 1.0)
