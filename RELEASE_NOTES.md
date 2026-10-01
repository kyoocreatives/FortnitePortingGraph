An unofficial fork of [FortnitePorting](https://github.com/h4lfheart/FortnitePorting) that rebuilds Fortnite's materials in Blender from their Unreal material graphs, and adds LEGO Fortnite and Rocket Racing content. Please don't report its problems on FortnitePorting's repository.

**Install:** download `FortnitePortingMP.exe` below and run it (Windows x64, no installer). Windows may warn about an unsigned app: *More info > Run anyway*. It runs beside an installed FortnitePorting, with its own settings (`%APPDATA%\FortnitePorting MP`) and its own Blender plugin (`fortnite_porting_mp`), installed from the Plugins page as in FortnitePorting. Exact materials need Blender 5.0 or newer; older Blender gets FortnitePorting's shaders.

**This release**
- Effects follow the item's styles: a style that swaps an outfit's effect or recolours it (Blackheart's stages, auras that only some styles have) now exports that effect, and the *Effects* pick shows on outfits whose effect comes from a style.
- Effects use more of what the game feeds their materials: colours and values the effect computes while it plays (Geno, Cerberus, Ares...), colour ramps from the effect itself (Voyager Unleashed's head flames), and materials and textures picked through the effect's own parameters (Renzo's hair).
- Effects on arms, hands and other bones are placed and oriented correctly with the Tasty rig and bone reorientation; a head's own effect no longer ends up at the feet.
- An effect that plays but stays invisible until the game raises one of its parameters (Salvador's flames) is reported in the log, with the parameters to set on the effect's empty before *Replay Effect*.
- Selecting an effect's particles in the viewport shows their material in the Material tab, ready to tweak.
- Cars: Mutable wheels' tire textures fixed, and a per-wheel arch control that moves the wheel's body area without moving the wheel.
- Exact materials: materials that read Material Attributes through functions no longer fail as a false loop (InfoInvader's head).

**What it adds**
- Exact materials, rebuilt from each material's Unreal graph. *Settings > Blender > Prefer FP Shaders for Characters* keeps FortnitePorting's shaders for character materials it has one for.
- Maps through Material Porter's map reader: actor components, overrides, custom primitive data, spline meshes, water bodies, level instances.
- Rocket Racing cars with their styles (tier, body colour, paint, decal, wheels).
- LEGO outfits (cooked and recipe figures, with face styles), LEGO emotes with animated faces, LEGO props and building sets, LEGO wildlife (needs LEGO Fortnite's optional content installed).
- No online account: sign-in, chat and FortnitePorting's updater are off. New releases of this fork are announced in the app.
