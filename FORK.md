# FortnitePorting, Material Porter fork

Branch `materialporter` on top of upstream `h4lfheart/FortnitePorting` (remote
`upstream`, base `cabc462d`, 2026-09-18). GPL-3.0 like upstream.

## What it adds

- **Exact materials.** The app serves Material Porter's bridge on
  `localhost:24320` once the game files are mounted: a material's description
  (instance chain, parameters, switches, masks), its UE graph and its
  functions' graphs (from the editor-only `.o.uasset` data), textures (the
  game's own BC data as DDS where Blender reads it) and parameter collections.
  The Blender plugin's material import (`processing/context/material_context.py`)
  calls `material_porter/hook.py` once FP has merged its parameters: the
  material is rebuilt from its graph by Material Porter's translator; FP's
  texture data and style overrides go over it as a variant. When that can't be
  done (no bridge, Blender < 5.0, a failed build) FP's own preset material is
  built as before. "Prefer FP Shaders for Characters" (Blender settings,
  Materials) gives FP the materials of outfits, back blings, pickaxes, gliders,
  pets, kicks and sidekicks that one of its shaders is made for (a base mapping
  matches, or its default shader knows the base colour); the rest stay exact.
  "Rim Light" (off by default) keeps the rim light of Fortnite's character
  materials (MF_RimV3's baseBrightness); off, exact materials get baseBrightness 0.
- **Maps through Material Porter's map reader.** FP's world export hands each
  level to the reader (`src/FortnitePorting.Exporting/MaterialPorter/MapReader.g.cs`):
  every component of an actor from the level's exports over its class's
  templates, attachments, overrides, dynamic material instances, texture
  data (textures and tints), custom primitive and per-instance data, level
  instances, spline meshes (bent in Blender), water bodies; HLODs, devices,
  hidden actors, ziplines and terrain-only (RVT) meshes are left out. A
  UEFN island's `_Generated_` cells, missing from its runtime hash, are read
  with its main level. Landscapes still export through FP, with their weight
  layers renamed to the LayerName the exact materials sample.
- **UEFN islands by map code (owner's builds only).** Keys from the user's key
  tool live in Material Porter's `islands.json` (shared by both apps, never
  logged); the Map page lists downloaded islands and has an Unlock Island box.
  `Fork.Islands` is on only when the git-ignored `src/FortnitePorting/Fork.local.props`
  sets `MPIslands`: releases and anyone else's builds don't export islands
  (upstream keeps it to the accounts it allows).
- **Rocket Racing cars.** Assets > Rocket Racing > Cars lists the car bodies.
  Styles (Tier, Body Color, Painted, Decal, Decal Color, Wheels) come from
  Material Porter's car assembly (`Exporting/MaterialPorter/Cars.cs`); the
  export is the body (the decal's material when one is picked), the wheels
  as children on its wheel sockets, and the values the car's Mutable program
  gives each material (`Mutable.g.cs`), sent to Blender as a Vehicle.
- **LEGO figures.** Assets > Lego > Outfits lists the LEGO outfits
  (`JunoAthenaCharacterItemOverrideDefinition`), all 2,480
  (`Exporting/MaterialPorter/Figures.cs`):
  - *cooked* (347): the skeletal mesh the AssembledMeshSchema lists
    (`SkeletalMeshes`), or the one in the figure's `Bake` folder, with its baked
    materials;
  - *recipes* (2,133): a CustomizableObjectInstance (Figure_X/Mutable/[Dataless/]COI_...) on the shared Mutable object
    `CO_Figure_Recipe_Dataless`, which builds only the body. `FigureRecipe.g.cs`
    dresses it as the game does: the body decoded from the program's streamed
    mesh ROMs (`MutableMeshes.g.cs`) with its stomach panel merged in and, for a
    hand/leg replacement, that part removed by the program's remove mask; written
    once per variant as `Assets/MaterialPorter/Figures/FigureBody_r<rev>[_NoLegR...].uemodel`
    by `UEModelWriter.g.cs`,
    its colour grid (32x32, the program's layout blocks, each part's
    `<part> Color` id through `T_LUT_Default`) and surface grid (`Tex Color M`: metal
    from the colour's LUT alpha or the block's `Metal Value`, see-through share, glow),
    deco and normal from the recipe (the skeleton body: one block's values over its grid);
    the head with its face material, a grid of its `Color Head ID`, its character
    accents (mustaches, beards) placed as the face rig does (the schema's CharacterAcc
    and BeardRegistration data: mouth + registration offset); each
    accessory or replacement part with the recipe material (or its override),
    a 2x2 colour grid, its surfaces' grid and its decos. Generated grids are served by the bridge as
    `/MaterialPorter/Generated/<name>` (MaterialService.GeneratedDir, `_Lin` ones linear).
  - parts go out as FP parts (body, head, the rest): the plugin merges them onto one armature
    (no Tasty rig: it is for the humanoid skeleton).
  - *expression*: Mouth, Eyes and Brows channels (the figure's own or one of the face rig's
    poses, DA_Figure_Face_Settings: 46/6/12) set the face material's pose parameters; a mouth
    pose moves the character accents with it. Mouth and brow options preview their atlas cells.
  Not done: cloth; teeth/tongue poses for an open mouth (the rig's, not known).
- **LEGO emotes.** Assets > Lego > Emotes (`JunoAthenaDanceItemOverrideDefinition`): the BR
  emote's name and icon, the figure's montage exported as an emote (sections, sounds, props)
  onto the selected figure's armature; its skeleton from its sequences (the montage names none).
  The face moves too: the sequences' face-rig curves (mouth, teeth, tongue, eyes, lashes, brows,
  accents) are keyed on the exact face material's inputs (`material_porter/face_anim.py`, an NLA
  track "MP Face" beside the body's strips; pose keys step as UE's do, `MPCurveModes`), and a
  mustache or beard follows the mouth by the rig's registration per mouth pose (`MPFaceRig`).
  Skinned meshes keep Blender's `rest_position`: UE's LocalPosition/PreSkinnedPosition on them is
  the reference pose (a face's print masks would slide as the figure sits).
- **LEGO props.** Assets > Lego > Props: building props and building sets
  (`JunoBuilding{Prop,Set}AccountItemDefinition`), the meshes of the actor each previews.
  Items whose actor isn't installed are hidden: most LEGO gameplay content is pakchunk60, an
  optional download (install tag GFP_JunoRoot, about 9 GiB) Fortnite installs for LEGO Fortnite.
- **LEGO wildlife.** Assets > Lego > Wildlife: each look of each creature (the pawn
  customizations under /JunoCreature_*, when the LEGO Fortnite content is installed). A look is
  either an AssembledMeshSchema - its skeletal meshes (body, head) as parts on one armature, the
  textures it puts on their materials (its colour LUT) as material values - or one skeletal mesh
  with override materials by slot.
  Their colour is a vertex colour's index into the LEGO LUT: exact since the translator
  samples a TF_Nearest texture Closest and its Time starts at 100 s (a building piece's
  damage flash, timed from a hit, was full red at Time 0).
- **Status line and log.** As in Material Porter's app: a status line under the
  window (a busy bar while an export runs or Blender imports, the latest step)
  and a Log drawer (`MaterialPorter/StatusLog.cs`, a Serilog sink): the export's
  steps, the bridge's answers to Blender, warnings, and what the Blender plugin
  reports while it imports (`material_porter/status.py` sends FP's log lines, the
  exact materials' notes and an import summary - meshes, materials, exact ones,
  seconds - to the bridge's `log` route, batched, never holding the import).
- **Faster world imports.** Exact materials of a world are laid out when a
  node editor first shows them; FP's per-object metadata scan, active-object
  switch and edit-mode Tris to Quads were replaced (a Hera cell 214 s to 89 s;
  a 30,105-object island 190 s).
- **No online account.** `SupabaseService` is inert (no client, no sign-in, no
  posted logins, exports or errors); the setup's sign-in step, the Online
  sidebar (Chat, Leaderboard) and the `fortniteporting://` registration are gone.

## Runs beside upstream FP

`MaterialPorter/Fork.cs` names what differs: settings in
`%APPDATA%\FortnitePorting MP`, data in `%LOCALAPPDATA%\FortnitePorting MP`,
single-instance pipe/mutex `FortnitePortingMP`, Blender plugin installed as
`scripts/startup/fortnite_porting_mp`, listening on port 40010 (upstream 40000),
bridge on 24320. `FORTNITEPORTING_MP_PROFILE=test` runs a separate instance
(own folders, lock, bridge 24322) for tests; its bridge's `/fork-export-world`
and `/fork-export-asset` routes return a level's or an asset's export as the
plugin receives it, `/fork-loader?type=` runs one Assets tab's loader (`check=1`: every listed figure's mesh resolved).
With both plugins in one Blender, the fork's panels/operators replace
upstream's same-named ones (Blender prints "registered before" infos).

## Releases

Pushing a tag `v<FP version>-mp.<N>` (`v4.0.0-mp.1`, then `-mp.2`...; after an
upstream merge `v4.0.1-mp.1`) runs `.github/workflows/build-release.yml`: a
single-file `FortnitePortingMP.exe`, released with `RELEASE_NOTES.md` as its
text (edit it before tagging). The `-mp.N` makes a release a dev build to FP's
updater, which would install upstream FP over it; `MaterialPorter/ForkUpdates.cs`
asks the fork's latest GitHub release instead, and builds without it (`-dev`,
a commit's) ask neither.

## Where the code lives

- `src/FortnitePorting/MaterialPorter/`: `MaterialPorterService.cs`, `Fork.cs`,
  `ForkUpdates.cs` (fork-only) and `*.g.cs` (generated).
- `plugins/Blender/fortnite_porting/material_porter/`: `hook.py`, `__init__.py`
  (fork-only) and the builder/translator modules (generated).
- Small edits in upstream files, each marked "Material Porter fork":
  `CUE4ParseService.cs` (bridge start, `ResolvedVersion`), `SupabaseService.cs`,
  `InstallationSetupViewModel.cs`, `AppWindow.axaml`, `AppService.cs`,
  `SettingsService.cs`, `Program.cs`, `BlenderInstallation.cs`,
  `ExportClientService.cs`, `AppWindowModel.cs`, `AssetVideoPreview.axaml.cs` (libvlc in a
  single-file build), `FortnitePorting.csproj`, `build-release.yml`, `README.md`, plugin
  `server.py` and `material_context.py`.

Generated files come from Material Porter (`Documents/Claude/materialporter`):
edit there, then `python tools/sync_fork.py`.

## Merging upstream

    git fetch upstream
    git merge upstream/main

Conflicts can only come from the small edits above.
