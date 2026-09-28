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
  built as before.
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
- **UEFN islands by map code.** Keys from the user's key tool live in Material
  Porter's `islands.json` (shared by both apps, never logged); the Map page
  lists islands for everyone and has an Unlock Island box.
- **Rocket Racing cars.** Assets > Rocket Racing > Cars lists the car bodies.
  Styles (Tier, Body Color, Painted, Decal, Decal Color, Wheels) come from
  Material Porter's car assembly (`Exporting/MaterialPorter/Cars.cs`); the
  export is the body (the decal's material when one is picked), the wheels
  as children on its wheel sockets, and the values the car's Mutable program
  gives each material (`Mutable.g.cs`), sent to Blender as a Vehicle.
- **LEGO figures (cooked bakes).** Assets > Lego > Outfits lists the LEGO
  outfits (`JunoAthenaCharacterItemOverrideDefinition`) whose figure is cooked
  as a skeletal mesh: the one its AssembledMeshSchema lists (`SkeletalMeshes`),
  or else the one in its figure's `Bake` folder beside its Mutable object
  (`Exporting/MaterialPorter/Figures.cs`). The export is that mesh with its
  baked materials, built exact. 347 of 2,480 figures are cooked this way; the
  rest exist only as Mutable objects, not read yet, and stay hidden.
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

## Where the code lives

- `src/FortnitePorting/MaterialPorter/`: `MaterialPorterService.cs`, `Fork.cs`
  (fork-only) and `*.g.cs` (generated).
- `plugins/Blender/fortnite_porting/material_porter/`: `hook.py`, `__init__.py`
  (fork-only) and the builder/translator modules (generated).
- Small edits in upstream files, each marked "Material Porter fork":
  `CUE4ParseService.cs` (bridge start, `ResolvedVersion`), `SupabaseService.cs`,
  `InstallationSetupViewModel.cs`, `AppWindow.axaml`, `AppService.cs`,
  `SettingsService.cs`, `Program.cs`, `BlenderInstallation.cs`,
  `ExportClientService.cs`, plugin `server.py` and `material_context.py`.

Generated files come from Material Porter (`Documents/Claude/materialporter`):
edit there, then `python tools/sync_fork.py`.

## Merging upstream

    git fetch upstream
    git merge upstream/main

Conflicts can only come from the small edits above.
