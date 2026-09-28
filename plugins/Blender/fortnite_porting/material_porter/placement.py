"""Material Porter fork: what Material Porter's map reader adds to a placed mesh
(the fork's MaterialPorterMesh fields), applied once FP has made its object."""
import bpy

from .meshes import spline_bend, white_colors


def after_import(mesh, obj, mesh_obj, scale):
    """A spline mesh's bend (UE's slice transform, on a copy of the mesh),
    white vertex colours where the mesh has none (what UE reads there), and
    the component's custom primitive data / an instance's custom data as
    the object properties the exact materials read (mp_cpd<i>, mp_pic<i>)."""
    target = mesh_obj if mesh_obj is not None else obj
    if target is None or target.type != 'MESH':
        return
    if sp := mesh.get("MPSpline"):
        target.data = spline_bend(target.data, sp, scale)
    if bpy.app.version >= (5, 0, 0):
        white_colors(target.data)
    if names := mesh.get("MPLayerNames"):
        # a landscape's weight layers, by the LayerName its materials sample
        for attribute in target.data.color_attributes:
            if attribute.name in names:
                attribute.name = names[attribute.name]
    if cpd := mesh.get("MPPrimitiveData"):
        target["mp_cpd"] = 1.0
        for j, x in enumerate(cpd):
            target["mp_cpd%d" % j] = float(x)
    if pic := mesh.get("MPInstanceData"):
        target["mp_pic"] = float(len(pic))
        for j, x in enumerate(pic):
            target["mp_pic%d" % j] = float(x)
