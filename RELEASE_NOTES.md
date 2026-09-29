An unofficial fork of [FortnitePorting](https://github.com/h4lfheart/FortnitePorting) that rebuilds Fortnite's materials in Blender from their Unreal material graphs, and adds LEGO Fortnite and Rocket Racing content. Please don't report its problems on FortnitePorting's repository.

**Install:** download `FortnitePortingMP.exe` below and run it (Windows x64, no installer). Windows may warn about an unsigned app: *More info > Run anyway*. It runs beside an installed FortnitePorting, with its own settings (`%APPDATA%\FortnitePorting MP`) and its own Blender plugin (`fortnite_porting_mp`), installed from the Plugins page as in FortnitePorting. Exact materials need Blender 5.0 or newer; older Blender gets FortnitePorting's shaders.

**This release:** a *Material Fixer* panel (3D View sidebar > Fortnite Porting, and the Shader Editor's sidebar) repairs FortnitePorting's shader materials (textures FP left unlinked, foliage alpha, wrong links, colour spaces), for the whole file or only the selected objects. UEFN island materials, which have no graph to translate, are built better: textures found by their names (a bare "BaseColor", ORM, Unity masks), a lone texture as the base colour, colour parameters, glows, glass; *Fix FP Materials* rebuilds the ones an earlier version made.

**What it adds**
- Exact materials, rebuilt from each material's Unreal graph. *Settings > Blender > Prefer FP Shaders for Characters* keeps FortnitePorting's shaders for character materials it has one for.
- Maps through Material Porter's map reader: actor components, overrides, custom primitive data, spline meshes, water bodies, level instances.
- Rocket Racing cars with their styles (tier, body colour, paint, decal, wheels).
- LEGO outfits (cooked and recipe figures, with face styles), LEGO emotes with animated faces, LEGO props and building sets, LEGO wildlife (needs LEGO Fortnite's optional content installed).
- No online account: sign-in, chat and FortnitePorting's updater are off. New releases of this fork are announced in the app.
