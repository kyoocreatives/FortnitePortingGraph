"""Wire FortnitePorting v4 materials whose textures ended up unlinked or in the wrong input.

(From the FP Material Fixer extension, fpisland/addon/fp_material_fixer/fixer.py.)

FP only maps texture parameters whose UE names it knows; everything else lands in an "Unused Textures"
frame. This classifies those textures by image name (then parameter label) and feeds the FPv4 groups:

- FPv4 Base Material.SpecularMasks reads R=Specular, G=Metallic, B=Roughness
  (SwizzleRoughnessToGreen=1 -> R=Spec, G=Rough, B=Metal, used for "_SRM" textures).
  ORM / RMA / Unity MaskMap / split roughness+metallic go through a Combine Color with the right channels.
- FPv4 Base Foliage.MasksTexture: R = alpha, B = subsurface. Its default 0.8 makes leaves 80% opaque.
Only empty inputs are filled; links that are clearly wrong (normal map into Diffuse...) are removed first.
"""
import os
import re
from collections import Counter, defaultdict

IMG_BLACKLIST = {
    'defaultdiffuse', 'flatnormal', 'flatnormalblue', 't_fortnite_default_s', 't_blacksrgb', 't_whitesrgb',
    't_midgreysrgb', 't_midgreymask', 't_whitemask', 't_flatgrey', 'defaulttexture', 'baseflattenlinearcolor',
    't_blank', 't_blank_legalwhite_srgbcolor', 'black', 't_ev_blankwhite_01', 'allred', 't_sfx_m',
    '3pxrgbimage', 't_default_basecolor_white', 'mask_test', 'white', 't_white_map_d', 't_sfx_grayscale',
    'defaultnormal', 't_blue_map_d', 't_black_map_bc', 't_macrovariation', 'tc_hdr01', 't_fill_b',
}
LABEL_BLACKLIST = re.compile(
    r'sfx|nanite|cover|top layer|snow|lichen|moss|detail|damage|noise|flake|chart|palette|colormask|biome|'
    r'blockout|wpo|position|x-axis|index|lut|puddle|projected|leaks|velvet|vegetable|dfx|vnr|forces|farwave|'
    r'normalandheight|overlay|wspaint|dirt|world aligned|macro|^l[2-9]|bg_|background|fallback|side 2|lerp|'
    r'anodizing|thinfilm|grayscale|irradiance|custom|random|^vp_|foliagemask|leaves_mask|mask texture|'
    r'texturemask|shininess|ambient|specularcolormap|occlusionmap|opacitymaskmap|(?<!param)[_ ][2-9]$|texture_[2-9]')

BASE_GROUPS = ('FPv4 Base Material', 'FPv4 Base Foliage')
LAYER_GROUPS = ('FPv4 Layer', 'FPv4 Base Layer')
DATA_SOCKETS = {'Normals', 'SpecularMasks', 'M', 'MasksTexture', 'Alpha', 'Roughness', 'Metallic', 'Specular'}
COLOR_SOCKETS = {'Diffuse', 'Emission', 'Background Diffuse'}


def stem(img):
    return os.path.splitext(re.sub(r'\.\d{3}$', '', img.name))[0].lower()


def img_class(img):
    s = stem(img)
    toks = s.split('_')
    tok = toks[-1]
    # Unity-authored packs: HDRP MaskMap (R metal, G AO, A smoothness), MetallicSmoothness (R metal, A smoothness)
    if tok == 'maskmap':
        return 'unitymask'
    if 'metallicsmoothness' in s:
        return 'metalsmooth'
    if tok in ('h', 'height', 'hm', 'disp', 'displacement') or 'height' in s or 'caustic' in s:
        return 'height'   # recognised so it is never mistaken for a base colour; not wired
    if tok == 'basemap' or ('color' in toks and 'normal' not in s):
        return 'diffuse'
    if 'occlusionroughnessmetallic' in s or tok in ('orm', 'orme', 'arm') or '_orm_' in s:
        return 'orm'
    if tok == 'rma':
        return 'rma'
    if 'normal' in s or tok in ('n', 'nm', 'nrm', 'norm'):
        return 'normal'
    if tok in ('s', 'srm', 'smr', 'spec', 'specular') or 'specularmask' in s:
        return 'srm'
    if 'roughness' in s or tok in ('r', 'rough'):
        return 'rough'
    if 'metallic' in s or 'metalness' in s or tok == 'metal':
        return 'metal'
    if 'opacity' in s or tok in ('alpha', 'opacity', 'op'):
        return 'opacity'
    if 'emissive' in s or 'emission' in s or tok in ('e', 'emissive'):
        return 'emissive'
    if tok in ('ao', 'occlusion') or 'ambientocclusion' in s:
        return 'ao'
    if any(k in s for k in ('basecolor', 'base_color', 'albedo', 'diffuse')) or tok in ('d', 'bc', 'c', 'co', 'col', 'color', 'diff'):
        return 'diffuse'
    if tok in ('m', 'msk', 'mask', 'masks'):
        return 'mask?'   # weak: FN mask or metallic, the label decides
    return None


def rough_in_green(img):
    # "_SRM" = Specular, Roughness, Metallic -> FP SwizzleRoughnessToGreen; FN "_S"/"_SMR" keep metal in green
    return stem(img).split('_')[-1] == 'srm'


def label_class(label):
    l = label.lower()
    if 'emis' in l or l == 'e':
        return 'emissive'
    if 'normal' in l:
        return 'normal'
    if l in ('rma', 'base rma'):
        return 'rma'
    if l in ('orm', 'orme') or 'ao_roughness_metallic' in l or 'occlusionroughnessmetallic' in l:
        return 'orm'
    if 'specularmask' in l or 'specular mask' in l or l in ('smr_texture', 'srm', 'smr') or 'specularroughness' in l or l.startswith('specular'):
        return 'srm'
    if 'roughness' in l:
        return 'rough'
    if 'metallic' in l:
        return 'metal'
    if 'opacity' in l or l == 'alpha':
        return 'opacity'
    if any(k in l for k in ('diffuse', 'difuse', 'albedo', 'basecolor', 'base color', 'base_color', 'colormap')) or l in ('bc', 'color', 'texture'):
        return 'diffuse'
    return None


def classify(node):
    ic = img_class(node.image)
    lc = label_class(node.label or '')
    if ic == 'mask?':
        return lc
    return ic or lc


def cand_priority(node):
    l = (node.label or '').lower()
    return (0 if re.search(r'(^|[^0-9])1$', l) or not re.search(r'\d$', l) else 1, l)


def base_node(mat):
    if not mat or not mat.node_tree:
        return None
    bases = [n for n in mat.node_tree.nodes if n.type == 'GROUP' and n.node_tree and n.node_tree.name in BASE_GROUPS]
    return bases[0] if len(bases) == 1 else None


class Run:
    def __init__(self, dry):
        self.dry = dry
        self.report = defaultdict(list)
        self.counts = Counter()
        self.cs_changes = []

    def act(self, mat, msg):
        self.report[mat.name].append(msg)


def _place(node, base, slot_idx):
    node.parent = None
    node.location = (base.location.x - 520, base.location.y - 40 - 300 * slot_idx)


def fix_material(mat, run):
    nt = mat.node_tree
    base = base_node(mat)
    if base is None:
        return
    if any(n.type == 'GROUP' and n.node_tree and n.node_tree.name in LAYER_GROUPS for n in nt.nodes):
        return   # layered materials are FP's own business
    kind = 'foliage' if base.node_tree.name == 'FPv4 Base Foliage' else 'base'
    links = nt.links
    dry = run.dry
    unlinked_now = set()

    # 1) wrong links: image class incompatible with the socket
    for l in list(links):
        if l.to_node != base or l.from_node.type != 'TEX_IMAGE' or not l.from_node.image or l.from_socket.name != 'Color':
            continue
        c = img_class(l.from_node.image)
        s = l.to_socket.name
        bad = (s in ('Diffuse', 'Background Diffuse') and c in ('normal', 'orm', 'rma', 'srm', 'rough', 'metal', 'ao')) or \
              (s == 'Normals' and c not in ('normal', None)) or \
              (s == 'SpecularMasks' and c in ('diffuse', 'normal', 'emissive', 'ao'))
        if bad:
            run.act(mat, f"unlinked wrong {l.from_node.image.name} ({c}) from {s}")
            run.counts['wrong links'] += 1
            unlinked_now.add(l.from_node.name)
            if not dry:
                links.remove(l)

    # 2) candidates: unlinked texture nodes
    cands = defaultdict(list)
    for n in nt.nodes:
        if n.type != 'TEX_IMAGE' or not n.image:
            continue
        if any(o.is_linked for o in n.outputs) and n.name not in unlinked_now:
            continue
        if stem(n.image) in IMG_BLACKLIST or LABEL_BLACKLIST.search((n.label or '').lower()):
            continue
        c = classify(n)
        if c:
            cands[c].append(n)
    for c in cands:
        cands[c].sort(key=cand_priority)

    def free(sock):
        s = base.inputs.get(sock)
        return s is not None and not s.is_linked

    def link(node, out, sock, desc, slot):
        run.act(mat, desc)
        run.counts[sock] += 1
        if not dry:
            links.new(node.outputs[out], base.inputs[sock])
            _place(node, base, slot)

    # Diffuse
    if cands['diffuse'] and free('Diffuse'):
        n = cands['diffuse'][0]
        link(n, 'Color', 'Diffuse', f"{n.image.name} [{n.label}] -> Diffuse", 0)
    elif free('Diffuse') and kind == 'base' and not any(cands.values()):
        # a single unnamed sRGB texture (label "Param" + a logo/poster image) is the base colour
        unk = [n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image and not any(o.is_linked for o in n.outputs)
               and classify(n) is None and stem(n.image) not in IMG_BLACKLIST
               and not LABEL_BLACKLIST.search((n.label or '').lower()) and n.image.colorspace_settings.name == 'sRGB']
        if len(unk) == 1:
            link(unk[0], 'Color', 'Diffuse', f"{unk[0].image.name} [{unk[0].label}] -> Diffuse (only texture)", 0)

    if cands['normal'] and free('Normals'):
        n = cands['normal'][0]
        link(n, 'Color', 'Normals', f"{n.image.name} [{n.label}] -> Normals", 1)

    if kind == 'base' and cands['emissive'] and free('Emission'):
        n = cands['emissive'][0]
        link(n, 'Color', 'Emission', f"{n.image.name} [{n.label}] -> Emission", 3)

    if cands['opacity']:
        n = cands['opacity'][0]
        if kind == 'base' and free('Alpha'):
            link(n, 'Color', 'Alpha', f"{n.image.name} [{n.label}] -> Alpha", 4)
        elif kind == 'foliage' and free('MasksTexture'):
            link(n, 'Color', 'MasksTexture', f"{n.image.name} [{n.label}] -> MasksTexture (alpha)", 4)

    if kind == 'base':
        _spec_base(mat, nt, base, cands, free, link, run)
    else:
        _spec_foliage(mat, nt, base, cands, free, link, run)

    # foliage alpha from the diffuse alpha channel when there is no opacity map
    if kind == 'foliage' and free('MasksTexture') and not cands['opacity']:
        d = base.inputs['Diffuse']
        dn = d.links[0].from_node if d.is_linked else None
        if dn is not None and dn.type == 'TEX_IMAGE' and dn.image and dn.image.channels == 4:
            run.act(mat, f"{dn.image.name}.Alpha -> MasksTexture (foliage alpha)")
            run.counts['MasksTexture'] += 1
            if not dry:
                links.new(dn.outputs['Alpha'], base.inputs['MasksTexture'])

    # grey foliage albedo meant to be tinted by a "Base Albedo Tint" parameter
    if kind == 'foliage':
        tint = next((n for n in nt.nodes if n.type == 'RGB' and n.label == 'Base Albedo Tint' and not n.outputs[0].is_linked), None)
        d = base.inputs['Diffuse']
        if tint and d.is_linked and d.links[0].from_node.type == 'TEX_IMAGE':
            tex = d.links[0].from_node
            run.act(mat, f"Diffuse = {tex.image.name} x Base Albedo Tint")
            run.counts['tints'] += 1
            if not dry:
                mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
                mix.inputs['Factor'].default_value = 1.0
                mix.label = 'Albedo x Tint'
                mix.location = (base.location.x - 220, base.location.y + 120)
                links.new(tex.outputs['Color'], mix.inputs['A'])
                links.new(tint.outputs['Color'], mix.inputs['B'])
                links.new(mix.outputs['Result'], d)


def _spec_base(mat, nt, base, cands, free, link, run):
    links = nt.links
    if not free('SpecularMasks') or base.inputs['SwizzleRoughnessToGreen'].is_linked:
        return
    if cands['srm']:
        n = cands['srm'][0]
        swz = 1 if rough_in_green(n.image) else 0
        link(n, 'Color', 'SpecularMasks', f"{n.image.name} [{n.label}] -> SpecularMasks ({'SRM, swizzle' if swz else 'FN S'})", 2)
        if not run.dry:
            base.inputs['SwizzleRoughnessToGreen'].default_value = swz
        return
    unity = cands['unitymask'] or cands['metalsmooth']
    if not (cands['orm'] or cands['rma'] or unity or cands['rough'] or cands['metal']):
        return
    src = (cands['orm'] or cands['rma'] or unity or [None])[0]
    layout = 'orm' if cands['orm'] else ('rma' if cands['rma'] else ('unity' if unity else 'split'))
    rough_n = cands['rough'][0] if layout == 'split' and cands['rough'] else None
    metal_n = cands['metal'][0] if layout == 'split' and cands['metal'] else None
    run.act(mat, f"{src.image.name} ({layout.upper()}) -> SpecularMasks via Combine" if src else
            f"roughness={rough_n.image.name if rough_n else '-'} metallic={metal_n.image.name if metal_n else '-'} -> SpecularMasks via Combine")
    run.counts['SpecularMasks'] += 1
    if run.dry:
        return
    comb = nt.nodes.new('ShaderNodeCombineColor')
    comb.label = 'FP SpecularMasks (S,M,R)'
    comb.location = (base.location.x - 220, base.location.y - 640)
    comb.inputs['Red'].default_value = 0.5
    comb.inputs['Green'].default_value = 0.0
    comb.inputs['Blue'].default_value = 0.5
    if src:
        sep = nt.nodes.new('ShaderNodeSeparateColor')
        sep.location = (base.location.x - 380, base.location.y - 640)
        _place(src, base, 2)
        links.new(src.outputs['Color'], sep.inputs['Color'])
        if layout == 'orm':        # R=AO G=Rough B=Metal
            links.new(sep.outputs['Blue'], comb.inputs['Green'])
            links.new(sep.outputs['Green'], comb.inputs['Blue'])
        elif layout == 'unity':    # R=Metal, A=Smoothness -> rough = 1 - A
            inv = nt.nodes.new('ShaderNodeMath')
            inv.operation = 'SUBTRACT'
            inv.inputs[0].default_value = 1.0
            inv.location = (base.location.x - 380, base.location.y - 820)
            links.new(src.outputs['Alpha'], inv.inputs[1])
            links.new(sep.outputs['Red'], comb.inputs['Green'])
            links.new(inv.outputs['Value'], comb.inputs['Blue'])
        else:                      # RMA: R=Rough G=Metal B=AO
            links.new(sep.outputs['Green'], comb.inputs['Green'])
            links.new(sep.outputs['Red'], comb.inputs['Blue'])
    else:
        if metal_n:
            _place(metal_n, base, 2)
            links.new(metal_n.outputs['Color'], comb.inputs['Green'])
        if rough_n:
            _place(rough_n, base, 5)
            links.new(rough_n.outputs['Color'], comb.inputs['Blue'])
    base.inputs['SwizzleRoughnessToGreen'].default_value = 0
    links.new(comb.outputs['Color'], base.inputs['SpecularMasks'])


def _spec_foliage(mat, nt, base, cands, free, link, run):
    links = nt.links
    src = (cands['orm'] or cands['rma'] or [None])[0]
    srm = cands['srm'][0] if cands['srm'] else None
    if src and (free('Roughness') or free('Metallic')):
        run.act(mat, f"{src.image.name} -> foliage Roughness/Metallic via Separate")
        run.counts['Roughness'] += 1
        if not run.dry:
            sep = nt.nodes.new('ShaderNodeSeparateColor')
            sep.location = (base.location.x - 250, base.location.y - 640)
            _place(src, base, 2)
            links.new(src.outputs['Color'], sep.inputs['Color'])
            r, m = ('Green', 'Blue') if cands['orm'] else ('Red', 'Green')
            if free('Roughness'): links.new(sep.outputs[r], base.inputs['Roughness'])
            if free('Metallic'): links.new(sep.outputs[m], base.inputs['Metallic'])
    elif srm and rough_in_green(srm.image) and (free('Roughness') or free('Metallic')):
        run.act(mat, f"{srm.image.name} (SRM) -> foliage Specular/Roughness/Metallic via Separate")
        run.counts['Roughness'] += 1
        if not run.dry:
            sep = nt.nodes.new('ShaderNodeSeparateColor')
            sep.location = (base.location.x - 250, base.location.y - 640)
            _place(srm, base, 2)
            links.new(srm.outputs['Color'], sep.inputs['Color'])
            for ch, sk in (('Red', 'Specular'), ('Green', 'Roughness'), ('Blue', 'Metallic')):
                if free(sk): links.new(sep.outputs[ch], base.inputs[sk])
    elif srm and free('SpecularMasks'):
        link(srm, 'Color', 'SpecularMasks', f"{srm.image.name} -> foliage SpecularMasks", 2)
        if not run.dry:
            base.inputs['UseSpecularMasks'].default_value = True
    elif cands['rough'] and free('Roughness'):
        n = cands['rough'][0]
        link(n, 'Color', 'Roughness', f"{n.image.name} -> foliage Roughness", 2)
    # RS (roughness/specular) packs, as used by some leaf cards: R=Specular, G=Roughness
    rs = next((n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image and not any(o.is_linked for o in n.outputs)
               and stem(n.image).endswith('_rs')), None)
    if rs and free('Roughness'):
        run.act(mat, f"{rs.image.name} (RS) -> foliage Specular/Roughness via Separate")
        run.counts['Roughness'] += 1
        if not run.dry:
            sep = nt.nodes.new('ShaderNodeSeparateColor')
            sep.location = (base.location.x - 250, base.location.y - 820)
            _place(rs, base, 5)
            links.new(rs.outputs['Color'], sep.inputs['Color'])
            if free('Specular'): links.new(sep.outputs['Red'], base.inputs['Specular'])
            links.new(sep.outputs['Green'], base.inputs['Roughness'])


def fix_colorspaces(materials, run):
    """Data maps feeding data inputs -> Non-Color; colour maps feeding colour inputs -> sRGB."""
    uses = defaultdict(set)
    for m in materials:
        if not m.node_tree:
            continue
        for l in m.node_tree.links:
            fn = l.from_node
            if fn.type != 'TEX_IMAGE' or not fn.image or l.from_socket.name != 'Color':
                continue
            tn, s = l.to_node, l.to_socket.name
            if tn.type == 'GROUP' and tn.node_tree and tn.node_tree.name.startswith('FPv4'):
                uses[fn.image].add('data' if s in DATA_SOCKETS else 'color' if s in COLOR_SOCKETS else 'other')
            elif tn.bl_idname in ('ShaderNodeSeparateColor', 'ShaderNodeCombineColor'):
                uses[fn.image].add('data')
            else:
                uses[fn.image].add('other')
    for img, u in uses.items():
        cs = img.colorspace_settings.name
        c = img_class(img)
        new = None
        if u == {'data'} and cs == 'sRGB' and c != 'diffuse':
            new = 'Non-Color'
        elif u == {'color'} and cs == 'Non-Color' and c in ('diffuse', 'emissive'):
            new = 'sRGB'
        if new:
            run.cs_changes.append((img.name, cs, new))
            if not run.dry:
                img.colorspace_settings.name = new
    run.counts['colour spaces'] = len(run.cs_changes)


def fix_materials(materials, dry=False):
    run = Run(dry)
    materials = [m for m in materials if m and m.node_tree and not m.library]
    for m in materials:
        fix_material(m, run)
    fix_colorspaces(materials, run)
    return run
