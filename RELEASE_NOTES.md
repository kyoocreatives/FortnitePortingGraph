An unofficial fork of [FortnitePorting](https://github.com/h4lfheart/FortnitePorting) that rebuilds Fortnite's materials in Blender from their Unreal material graphs, and adds LEGO Fortnite and Rocket Racing content. Please don't report its problems on FortnitePorting's repository.

**Install:** download `FortnitePortingMP.exe` below and run it (Windows x64, no installer). Windows may warn about an unsigned app: *More info > Run anyway*. It runs beside an installed FortnitePorting, with its own settings (`%APPDATA%\FortnitePorting MP`) and its own Blender plugin (`fortnite_porting_mp`), installed from the Plugins page as in FortnitePorting. Exact materials need Blender 5.0 or newer; older Blender gets FortnitePorting's shaders.

**This release**
- *Effects* tab (Assets > Gameplay > Effects): 18,500 particle effects, played in Blender from their own compiled scripts (sprites, meshes, ribbons, decals, lights as point lights), looping by default. *Replay Effect* plays one again over another frame range.
- Items with their own effects: an *Effects* pick on pickaxes (trail, swing, idle and hit effects, played along a swing animation), back blings, outfits, gliders (trails), weapons and sprites (a fire sprite's flames).
- *Animations* tab: the game's animations sorted by what they belong to (characters, gliders, back blings, creatures...), with their owner's icon and name, and filters.
- Control rigs, with the Tasty rig setting on (or the Rig panel in the sidebar): creatures and sidekicks (leg and arm IK, footprint controls, eye aim), LEGO figures, and vehicles (drive, steer, drift, a roof control for the body and suspension, a ring per wheel, wheels that follow a ground mesh).
- Sprites: their effect and second material; the black sprites (Spooky Dash, Vampire...) and Fire Sprite's glow fixed; variants' fades closer to their icons.
- Exact materials: several translation fixes (division by zero on vectors, view space, Object Position, Power of a negative base, vector parameters with negative values).

**What it adds**
- Exact materials, rebuilt from each material's Unreal graph. *Settings > Blender > Prefer FP Shaders for Characters* keeps FortnitePorting's shaders for character materials it has one for.
- Maps through Material Porter's map reader: actor components, overrides, custom primitive data, spline meshes, water bodies, level instances.
- Rocket Racing cars with their styles (tier, body colour, paint, decal, wheels).
- LEGO outfits (cooked and recipe figures, with face styles), LEGO emotes with animated faces, LEGO props and building sets, LEGO wildlife (needs LEGO Fortnite's optional content installed).
- No online account: sign-in, chat and FortnitePorting's updater are off. New releases of this fork are announced in the app.
