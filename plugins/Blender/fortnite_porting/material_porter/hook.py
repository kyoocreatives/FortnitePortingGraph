"""Material Porter fork: FP's materials rebuilt exactly from their UE graphs.

FP's material import calls build_exact() once its parameters are merged. The
FP app's Material Porter bridge (localhost:24320) describes the material and
serves its graph, functions, textures and parameter collections; the builder
(build.py, translated by ue_graph.py) makes the node trees. When that can't
be done (no bridge, Blender before 5.0, a failed build) it returns None and
FP builds its own preset material as usual.
"""
import hashlib
import json
import os
import traceback

import bpy

from . import build
from .app_client import AppClient, AppError
from ..logger import Log

URL = os.environ.get("MATERIAL_PORTER_BRIDGE", "http://localhost:24320")

# one FP import at a time: its client (a memo of what the app answered), what
# it built, and the shapes new instances can copy
_job = {"key": None, "app": None, "down": False, "built": {}, "shapes": {}, "notes": []}


def _session(context):
    """The current import's state, begun afresh for each import (context)."""
    if _job["key"] is not context:
        build.begin_session()
        _job.update(key=context, app=AppClient(URL), down=False, built={}, shapes={}, notes=[])
    return _job


def _log(message):
    Log.info("[Material Porter] " + message)


def _texture_path(texture):
    return (texture or {}).get("Path")


def _overlay(texture_data, override_parameters, values=None):
    """What FP puts over the material's own values: a building's texture data
    (by layer, like FP), a style's parameter overrides, and the fork map
    reader's values (a dynamic instance's, a building's texture data)."""
    textures, scalars, vectors = {}, {}, {}
    if values:
        textures.update(values.get("Textures") or {})
        scalars.update(values.get("Scalars") or {})
        vectors.update(values.get("Vectors") or {})
    for data in texture_data or []:
        index = data.get("Index") or 0
        ts = "_Texture_%d" % (index + 1) if index > 0 else ""
        ss = "_%d" % (index + 1) if index > 0 else ""
        for key, name in (("Diffuse", "Diffuse" + ts), ("Normal", "Normals" + ts), ("Specular", "SpecularMasks" + ss)):
            if path := _texture_path(data.get(key)):
                textures[name] = path
    for parameters in override_parameters or []:
        for t in parameters.get("Textures") or []:
            if path := _texture_path(t.get("Texture")):
                textures[t.get("Name")] = path
        for s in parameters.get("Scalars") or []:
            scalars[s.get("Name")] = float(s.get("Value") or 0.0)
        for v in parameters.get("Vectors") or []:
            c = v.get("Value") or {}
            vectors[v.get("Name")] = [c.get("R", 0.0), c.get("G", 0.0), c.get("B", 0.0), c.get("A", 1.0)]
    return {k: v for k, v in (("textures", textures), ("scalars", scalars), ("vectors", vectors)) if v}


def _built(path, variant):
    return next((m for m in bpy.data.materials if m.get(build.KEY_PATH) == path
                 and m.get(build.KEY_VARIANT, "") == variant and build.KEY_REPLACES not in m
                 and m.get(build.KEY_REV, 1) >= build.BUILD_REVISION), None)


def _material(job, entry, obj):
    """Built once per (path, variant) and import; an instance of a shape
    already built is a copy of it with its own values and images."""
    app, built, shapes, notes = job["app"], job["built"], job["shapes"], job["notes"]
    variant = entry.get("variant", "")
    key = (entry["path"], variant)
    if key in built:
        return built[key]
    mat = _built(*key)
    if mat is None:
        shape = build.shape_key(entry)
        src = shapes.get(shape) or build.find_shape(shape)
        if src is not None:
            try:
                made = build.build_like(src, dict(entry, target={}, variant=variant), app)
                if made is not None:
                    mat = made[0]
                    shapes[shape] = src
            except Exception as e:
                notes.append("%s: copy of %s failed (%s), built instead" % (entry["name"], src.name, e))
                mat = None
    if mat is None:
        mat, n = build.build_one(dict(entry, target={}, variant=variant), app, [obj] if obj else [])
        if mat.get(build.KEY_SHAPE):
            shapes.setdefault(mat[build.KEY_SHAPE], mat)
        notes += ["%s: %s" % (entry["name"], x) for x in n]
    built[key] = mat
    return mat


def exact_available(context):
    """Whether this import builds exact materials (Blender 5, and the app's bridge hasn't failed it)."""
    return bpy.app.version >= (5, 0, 0) and not _session(context)["down"]


def build_exact(context, material_data, texture_data=None, override_parameters=None, obj=None):
    """The exact Blender material for FP's material data, or None (FP's presets then)."""
    if bpy.app.version < (5, 0, 0):
        return None
    job = _session(context)
    if job["down"]:
        return None
    path = material_data.get("Path")
    if not path:
        return None
    try:
        entry = dict(job["app"].get("material", path))
    except AppError as e:
        if "isn't answering" in str(e):
            job["down"] = True
            _log("the FP app's bridge isn't answering at %s: FP's own materials this import" % URL)
        else:
            _log("%s: %s - FP's own material" % (material_data.get("Name"), e))
        return None
    overlay = _overlay(texture_data, override_parameters, material_data.get("MPValues"))
    if overlay:
        for kind, values in overlay.items():
            entry[kind] = dict(entry.get(kind) or {}, **values)
        entry["variant"] = hashlib.sha1(json.dumps(overlay, sort_keys=True).encode("utf-8")).hexdigest()[:8]
    # a world's hundreds of materials: each tree laid out when a node editor first shows it
    build.LAZY_LAYOUT = getattr(getattr(context, "type", None), "name", "") in ("WORLD", "PREFAB")
    try:
        mat = _material(job, entry, obj)
    except Exception as e:
        at = traceback.extract_tb(e.__traceback__)[-1]
        _log("%s: not built (%s: %s, at %s:%d) - FP's own material" % (
            material_data.get("Name"), type(e).__name__, e, os.path.basename(at.filename), at.lineno))
        return None
    finally:
        build.LAZY_LAYOUT = False
    for note in job["notes"]:
        _log(note)
    job["notes"].clear()
    # a LEGO figure's face: where its rig puts the character accents for each mouth pose (face_anim.py)
    if rig := material_data.get("MPFaceRig"):
        mat["mp_face_rig"] = rig
    return mat
