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
- **No online account.** `SupabaseService` is inert (no client, no sign-in, no
  posted logins, exports or errors); the setup's sign-in step, the Online
  sidebar (Chat, Leaderboard) and the `fortniteporting://` registration are gone.

## Runs beside upstream FP

`MaterialPorter/Fork.cs` names what differs: settings in
`%APPDATA%\FortnitePorting MP`, data in `%LOCALAPPDATA%\FortnitePorting MP`,
single-instance pipe/mutex `FortnitePortingMP`, Blender plugin installed as
`scripts/startup/fortnite_porting_mp`, listening on port 40010 (upstream 40000).
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
