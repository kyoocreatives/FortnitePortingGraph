An unofficial fork of [FortnitePorting](https://github.com/h4lfheart/FortnitePorting) that rebuilds Fortnite's materials in Blender from their Unreal material graphs, and adds LEGO Fortnite and Rocket Racing content. Please don't report its problems on FortnitePorting's repository.

**Install:** download `FortnitePortingMP.exe` below and run it (Windows x64, no installer). Windows may warn about an unsigned app: *More info > Run anyway*. It runs beside an installed FortnitePorting, with its own settings (`%APPDATA%\FortnitePorting MP`) and its own Blender plugin (`fortnite_porting_mp`), installed from the Plugins page as in FortnitePorting. Exact materials need Blender 5.0 or newer; older Blender gets FortnitePorting's shaders.

**This release**
- Effects render in Cycles as they do in EEVEE: glows no longer fade each other out, overlapping particles no longer turn black, stacked flame layers draw in the game's order (Elite Jules' leg flames), and flame strips that turn to the camera show up (her shoulder flames).
- Soft particles: effects fade where they meet what's behind them, as in game (sprites crossing the ground, a character's effect meshes against the body). An effect whose fade distance the game leaves at 0 stays fully drawn (Elite Jules' crown).
- Smoke lit by the game's lighting volume (round puffs) is lit softly, without its sphere's shading.
- Effects use the values the system hands its stateless emitters while it plays; an outfit's effect starts once the character's skeleton is in place; materials that tint what's behind them under UE 5's name for it (Tempest's eye glow) work.
- Exact materials: more of Fortnite's sky and cloud expressions translate (sky atmosphere light, aerial perspective, volumetric cloud inputs).
- Fixed: an import right after an update could fail with an unknown export type when the automatic Blender plugin sync hadn't finished.

**What it adds**
- Exact materials, rebuilt from each material's Unreal graph. *Settings > Blender > Prefer FP Shaders for Characters* keeps FortnitePorting's shaders for character materials it has one for.
- Maps through Material Porter's map reader: actor components, overrides, custom primitive data, spline meshes, water bodies, level instances.
- Rocket Racing cars with their styles (tier, body colour, paint, decal, wheels).
- LEGO outfits (cooked and recipe figures, with face styles), LEGO emotes with animated faces, LEGO props and building sets, LEGO wildlife (needs LEGO Fortnite's optional content installed).
- No online account: sign-in, chat and FortnitePorting's updater are off. New releases of this fork are announced in the app.
