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
  Time of day: the bridge takes the season's day sequence (the newest
  `DS_BR_Ch<n>S<n>`) at 12:00. Its parameter collection track sets
  FortniteMaterialParameters' cloud colours and overcast (the asset's own are
  black placeholders), and its sky mesh's material tracks set a sky dome's
  material (a master named `M_Sky*`, "Skydome" slot) and its sun and moon's
  ("SunMoon"). What the sky and baked clouds read from the renderer (the sky
  atmosphere, the dome at infinity, the clouds' altitude) is still stubbed:
  fpv4_clouds builds those.
- **Convert to Exact Materials.** 3D View sidebar > Fortnite Porting > Exact
  Materials > Selected / Scene (`operator/convert_op.py`): materials an FP import
  made before (FortnitePorting's own, or the fork's FP shaders) become exact.
  An FP material keeps only its asset's name (`OriginalName`); the bridge's
  `find-materials` route gives the material assets with that name (where two
  share it, the one whose textures the FP material shows) and the material
  import's `build_exact` makes the exact one, which takes the FP material's
  slots. What a style put over the material (a colour swap, a building's
  texture data) isn't kept on FP's material, so the exact one has the asset's own.
  Needs the app running with the game loaded.
- **Material Fixer.** Same tab, "Material Fixer" (also in the Shader Editor's
  sidebar); All Materials or Selected Objects; the report goes to Text Editor >
  "FP Fixer Report". It is the FP Material Fixer extension (fpisland) moved in
  (`operator/fixer/`):
  - *Fix FP Materials* (every build): FP's shader materials get the textures FP
    left in "Unused Textures" (BC / ORM / RMA / SRM / Unity masks, split
    roughness and metallic), foliage alpha, wrong links removed and colour
    spaces set; only empty inputs are filled. The fork's own island materials
    (no graph to translate: `material_porter/fallback.py` builds them from the
    cooked textures and values, `mp_fallback` = its revision) that an older
    builder made are built again (with the app open) and take their slots.
    *Preview Only* just reports.
  - The fallback builder follows the FP Material Fixer's rules: a texture's role
    from its name first (T_X_BC, a bare "BaseColor", "..._OcclusionRoughnessMetallic",
    a Unity MaskMap), then its parameter's (islands name theirs "Param", "Param_1");
    a lone unnamed texture is the base colour; the first base colour parameter
    (Color, MainColor, Param...), one brighter than 1 as a glow, a "Black"
    material black, a light's colour as emission, glass see-through.
  - *Recover Island Textures* (the owner's builds: shown only where the app's
    `fork-caps` says islands are on, and its routes refuse otherwise): a UEFN
    map's FP materials with no base colour. Where fpisland ran its own
    CUE4Parse tool with keys read from FP's logs, the app answers from what it
    has mounted (`IslandMaterials.cs`, routes `fork-island-find`, `-mesh`,
    `-material`): the material each was made from (islands this file has
    textures from first, then other islands, then the game's; same-named ones
    told apart by the meshes' slots, then by FP's leftover values), its
    instance parameters, its master's cooked ReferencedTextures and parameter
    defaults. Its textures come over the bridge's texture route; flat colours,
    roughness, glass opacity and light colours are set from the defaults.
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
- **Weapons as the game draws them** (`ExportContext.Weapons.cs`). FP exports an item's
  mesh with the mesh's own materials; two more things make a weapon's look:
  - its actor class's weapon mesh component (`WeaponActorClass` defaults > `WeaponMesh`,
    through the parent classes): `OverrideMaterials` and `CustomPrimitiveData`. A Morphite
    weapon is the plain weapon's mesh with `MI_Morphite_*` and data `[0, 1, 0]`; slot 1 is
    its `WeaponPhase` (1 to 4: how far the crystal has grown; `mp_cpd1` on the object).
  - a wrap, here the item's own (`IntrinsicOverrideWrap`: an exotic's glow). A wrap is one
    instance of `M_FN_Customization_MASTER`, which only carries the customization layer
    (`MF_Base_Customization`) the weapon masters run too, over a dummy base. The game lays
    its values over the weapon's own material; so does the export: the wrap's scalars,
    vectors, textures and static switches ride on the material as `MPValues` (`ParamSet`
    has `Switches` for it; `hook._overlay` applies them), but for the textures the weapon's
    material sets itself (diffuse, normals, masks, `CustomizationMask (_CM)`).
  Exact materials only: FP's own presets don't show either.
  Test routes: `fork-find-assets?path=&class=`, `fork-dump?path=[&full=1]`.
- **Wraps on the asset's page.** A weapon's or a vehicle's page has a Wrap list (the searchable
  picker, like a car's wheels): "Default" (the item as it is, an exotic's own wrap included),
  "None" where the item has one, then every wrap (`AthenaItemWrapDefinition`, about 1,200; their
  names and icon tiles are read once per run). Only on what takes wraps: an asset one of whose
  materials sets the customization mask (`Wraps.Supports`: weapons, the ATK, the Baller; not a
  potion or the battle bus). The pick (`WrapStyleData` > `ExportWrapStyle`) goes over every mesh
  of the export, a weapon's mods too (`MeshExport.ApplyWrapPick`), as `MPWrap` on each material;
  the plugin lays it over the material's own values and `MPValues`, but for the textures those
  set. A built material keeps what it was built with (`mp_overlay`) and its wrap's name
  (`mp_wrap`): "Remove Wrap from Selected" (sidebar > Fortnite Porting > Exact Materials,
  `material_porter/wrap.py`) takes the wrap off in Blender. The values a wrap's projection needs
  of the weapon (`DO NOT OVERRIDE-OBMIN`...) are the weapon's material's own.
- **Weapon mods** (`MaterialPorter/WeaponMods.cs`). A weapon item lists the mods it comes with
  (its DataList's `WeaponModSlots`: a magazine, a foregrip, its sights); FP exported the bare
  mesh. Now they come with it, and the weapon's page has a list per slot (Optic, Magazine,
  Barrel, Underbarrel): "Default" (the weapon's own), "None", and the mods that allow the weapon
  (each mod's `AllowedWeaponTagQuery`, a gameplay tag query's token stream, against the weapon's
  tags) and have a mesh for it; other weapons' iron sights are left out, and same-named mods say
  which family they are (`WeaponMods.Labels`). A mod's mesh, attach point and offset on a weapon
  are the row of the `*WeaponModOverrideData` tables (20 of them) for the weapon's tag and the
  mod's tag (the most specific weapon tag wins), else the mod's `DefaultModData`. The attach
  point (`attach_mag`, `attach_optic`, `attach_barrel`, `attach_under`, `attach_side`) is a bone
  of the weapon's skeleton or a socket on one: the export places the mod there in the weapon's
  space (the reference pose) as a child mesh with `MPParentBone`, and the plugin parents it to
  that bone (`placement.follow_bone`), so it follows the weapon's animation. A weapon with
  `bModsHidden` shows none of its own. `WeaponModStyleData` > `ExportWeaponModStyle`.
  Test routes: `fork-weapon-mods?path=`, `fork-asset-page?type=&name=[&pick=Channel:Option;...][&export=1]`
  (an asset's page as the Assets view builds it, and its export through `ExportService`'s styles),
  `fork-export-asset` takes `wrap=` and `mods=Slot:path;...`.
- **Animations tab.** Assets > Gameplay > Animations lists the game's 69,600 animations (sequences
  and montages), which the cooked registry mostly leaves out (2,248, nearly all islands'): found by
  path (an animation folder, "anim" or "montage" in the name: 133,000 packages of 2 million), each
  checked from its package's export and import maps without being read (`MaterialPorter/Animations.cs`:
  its class, its skeleton - the import of class Skeleton) and listed unread like the effects. The
  description says montage or sequence, its skeleton and its folder; the filters say what it is
  for, from its path and skeleton (Emotes, Characters, Gliders, Weapons, Creatures, Pickaxes, LEGO,
  Back Blings, Pets, Vehicles, Other). Reading the maps takes about 45 s the first time; the
  outlines are kept in `.data/mp_animations.tsv` (keyed by the file and archive counts), after
  which the tab lists in about a second. An export goes onto the armature selected in Blender, as
  from the Files tab (`AnimExport` reads the asset the tab listed unread).
  An animation of an item shows the item's name and icon ("Vanguard Squadron X-wing · Maverick
  Closed GLIDER"), and the tab sorts by name, so an item's animations sit together and the search
  finds them by the item's name (`MaterialPorter/AnimationOwners.cs`, `Animations.Assign`): a
  glider's, a back bling's and a pickaxe's by the skeleton of the item's mesh (from the mesh's
  package's imports; a skeleton a few items share is theirs together, one of more than 3 none), an
  emote's by its montage's folder, and the animations beside or under one found so (a glider's
  rider's) take its item too - 26,400 of the 69,600. The "Of an Item" filter keeps those. The
  items' index is kept in `.data/mp_animation_owners.tsv` beside the outlines.
- **Particle effects, played.** Assets > Gameplay > Effects lists 18,500 Niagara systems: the asset
  registry's (552 once each - the registry holds most of them twice - nearly all islands') and the
  game's own, found by file name (`NS_...`: the cooked registry leaves nearly all of them out;
  `Effects.ListedSystems`, the loader's `MPUnregistered`). Each is listed unread
  (`MaterialPorter/Unloaded.cs`: a stand-in object with the asset's name, path and class; the
  export reads the asset - holding them all read took over 40 GB): its package's export map says
  its class, its emitters, how many run on the GPU and what its renderers draw
  (`Effects.ReadOutline`), which is the item's description; the listing takes a few seconds. The
  engine's own templates (`/Niagara/`: not cooked to play) are left out. The tab's filter "Plays in
  Blender" leaves out the systems with nothing to replay (655: all on the GPU, or no emitter; most
  islands' effects are GPU). The Files tab
  exports any of them too (`DetermineExportType` falls back on the tabs' classes). An effect's export
  (`ExportContext.Effects.cs`, `MaterialPorter/Effects.cs`) is a tree: an empty per enabled emitter
  (tagged CPU, GPU or Stateless), and under it what its renderers draw - a mesh renderer's meshes
  with the materials it puts on them, a sprite or ribbon renderer's material on a plane the plugin
  makes (`material_porter/effects.py`), a decal renderer's on a quad across its projection. A
  renderer's own material parameters ride on the material as `MPValues`.
  Test routes: `fork-loader?type=Effect&described=1` (the listing, its descriptions, how many play),
  `fork-export-asset?type=Effect&listed=1&path=<object path>` (the export of the item as listed).
  - **CPU emitters are replayed.** A CPU emitter keeps its compiled scripts in the cooked asset
    (VectorVM bytecode). The system's node carries the package's exports (`Effects.Program`) and
    the vector fields its scripts sample (`Effects.Fields`); the plugin runs them:
    `niagara_vm.py` is the VM in numpy (every particle at once, as the engine does), `niagara.py`
    what the engine does around the scripts (the system's spawn and update scripts, each emitter's
    update then spawn, events between emitters, the constant blocks laid out as the engine's
    structs, the parameter stores as cooked, data interfaces: curves, arrays, vector fields,
    particle reads, renderer info, camera). `niagara_stateless.py` works a stateless emitter's
    particles out from its modules' settings (same ranges and curves, not the engine's random
    draws). `effect_replay.py` runs the system over the scene's frame range (a whole number of
    ticks per frame, 60 a second or so; it stops where the system completes) and keeps every
    frame's particles as a mesh of points per drawn piece; a geometry nodes modifier
    ("MP Effect Particles") keeps the current frame's points and instances the piece on each,
    turned as the renderer says (Turn), and "MP Effect Ribbons" strings a ribbon's points into
    ribbons. The modifier's Start Frame and Loop move and repeat the replay.
  - **What a particle gives its material** reaches it as instance attributes (the exact materials'
    Attribute nodes are of type Instancer, which falls back on the object's own properties on a
    still piece): `mp_particle` = 1 with `mp_particle_color`, `mp_dynamic` (four flags: which
    Dynamic Parameters the emitter writes, from the renderer's MaterialParamValidMask) with
    `mp_dynamic0..3`, `mp_subimage` (a flipbook's frame: a sprite's material carries `MPSprite`,
    its sub-image counts, and its UV0 picks the sub-image), `mp_age`, `mp_velocity` (Particle Speed,
    Particle Direction), `mp_size`, `mp_spin` (Particle Sprite Rotation: radians, degrees).
    A ribbon's run along it: its material (`MPRibbon`) reads them off the ribbon's mesh.
  - **Effect materials.** The raymarched smoke's Custom node (`MF_Raymarched_Smoke_Func`: a light
    march through the density texture in the sprite's plane) is laid out step by step, as many
    steps as the material's NumSteps says when it is built (`custom_raymarch_2d`; 64 at most). A
    piece whose material FP's importer hides (an anime outline's shell, `M_AnimeOutline_FX`: its
    ink lines come from the scene's depth) isn't drawn (`mp_effect_skip`) instead of showing as a
    white shell.
  - **World Position Offset.** A particle's material also moves its vertices (a lightning or flame
    mesh is a zero-width strip its material thickens towards the camera - `SplineThicken` - or
    bends): for an effect's pieces (`entry["particle"]`, set by `hook.build_exact` from the piece's
    `mp_effect`) the material's World Position Offset pin is translated too and wired to the
    material's Displacement (`Vector Displacement`, world space, cm and UE's axes turned to metres
    and Blender's; `displacement_method = 'DISPLACEMENT'`: Eevee moves the vertices). At the
    vertices the Geometry node's Incoming is zero, so a particle material's camera vector is the
    Camera Data's view vector turned into the world (`env._incoming`). Both sides are drawn (a
    thickened strip has no side of its own). Only materials with separate output pins: one that
    ends in a Material Attributes pin keeps its vertices.
  - **Division by zero.** UE's `A / B` with B zero is an infinity of A's sign, which a saturate
    turns into 1 or 0 (a camera fade over a length of 0 shows everything); a Blender Divide gives 0
    (the glow drew nothing). `Translator.divide` checks a socket divisor as the material runs. A
    SmoothStep over an empty range (Min = Max: a softness of 0.5 taken off both ends) is likewise a
    hard edge at Min, where a Map Range gives 0 (`Translator.smoothstep`: embers drew nothing).
  - **Particle Random** is the particle's own random number (`mp_random`, the renderer's
    `MaterialRandom`: the same for the particle's whole life), not the instance's, which changes as
    particles die.
  - **UE's mesh particle normals.** UE turns a mesh particle's normals by its scale, not by the
    scale's inverse (a sphere flattened into a camera-facing card keeps a round one's falloff: the
    explosions' mesh smoke). The modifier sets the normals that come out so.
  - **On a character, and again.** What the replay takes is kept with the effect (a text in the
    file: `effect_replay.store`), and the panel's **Replay Effect** runs it again over the scene's
    frame range: from the empty's `Start Frame`, with its `User.*` properties (the system's user
    parameters). With an armature selected too, the effect is put on it first
    (`effect_replay.attach`): its scripts then read that armature's bones and sockets frame by
    frame (`niagara.Skeleton`, `System.place`), and an effect that moves (its parent's animation
    or its own) leaves its world-space particles where they were spawned, as a trail.
  - **Contrails.** Assets > Cosmetics > Contrails lists the skydiving contrails with a Niagara
    effect (267); the export is the item's effect with the locker's flags on
    (`User.bIsFrontEnd`, `User.bIsFrontEndPreview`: the character needn't fall), put on the
    armature selected in Blender when it is sent.
  - **A pickaxe's own effects.** A pickaxe's page has an Effects pick (None, or "Its trail, swing,
    idle": what its weapon definition's data names - `AnimTrailsNiagara`, `SwingEffectNiagara`,
    `IdleEffectNiagara`; `AssetInfo.AddEffectStyles`, `ExportEffectsStyle`). With it the export puts
    each effect under the pickaxe's mesh (`ExportContext.PickaxeEffects`): the swing's and the idle's
    on their sockets (`SwingFXSocketName`, `IdleFXSocketName`: `MPParentBone`, `effects.on_bone`),
    the trail told the two sockets it runs between (`AnimTrailsFirstSocketName`,
    `AnimTrailsSecondSocketName`: `Sockets`, which stand for the trail's filtered sockets where the
    pickaxe has none of their names). They are replayed on the pickaxe's armature (its sockets are
    imported as bones). A trail and a swing effect show on a swing: animate the pickaxe, set the
    effect's `Start Frame` on the swing, select the pickaxe and press Replay Effect (it replays the
    effects on the selection). What isn't played of an effect that is on something (GPU emitters)
    is hidden instead of rowed beside it. Nothing moving above an effect: one frame is sampled
    for all (`effect_replay.Stand`). Test: `fork-export-asset?type=Pickaxe&effects=1`.
  - **Other items' own effects.** The same Effects pick is on a back bling's, an outfit's, a
    glider's and a weapon's page, only where the item has effects (`Effects.OwnEffectNames`; a part
    naming a blank system - `NS_Blank_Body`, `NS_Empty`: no emitter, there to switch a base part's
    effect off - has none: `Effects.Shown`):
    - a back bling's and an outfit's parts' idle effect (`IdleEffectNiagara` on `IdleFXSocketName`:
      `ExportContext.PartEffects`, from `CharacterPart`);
    - a glider's trails (`TrailEffectDefinitions`: system, socket, offset; the older
      `TrailEffectNiagara` / `2`: `GliderEffects`), sent with the locker's flags once the glider is
      out (`User.bIsFrontEnd`, `User.bIsFrontEndPreview`, `User.bIsFullyDeployed`: a speed line's
      opacity waits for the last; properties on the effect's empty, like
      `User.ForwardDot` and `User.RightDot`, which the game sets from the player's steering: change
      them and Replay Effect). A glider flies toward +Y in Blender; many trails take a second or
      two to start (the glider opening);
    - a weapon's actor class's Niagara components (its parent classes' too: `WeaponComponents`,
      `WeaponEffects`, from `WeaponLook`), each on what its construction script node attaches it to
      (`SCS_Node.AttachToName`) at the component's relative transform (`Place`). One that plays by
      itself (`bAutoActivate`) is played; one the game plays on an event (a reload, a level up) has
      the role "event": stored, its pieces hidden, played by Replay Effect from its `Start Frame`.
    Each effect carries its mesh's sockets (`ExportContext.MeshSockets`, kept per exported mesh while
    the pick is on: a skeletal mesh's own and its skeleton's, each on its bone; a static mesh's, on
    the mesh itself: `Table`), for a socket the armature doesn't have as a bone and for the ones the
    effect's scripts read (`effect_replay.holder_of`: the armature, else the static mesh it is on).
    An effect's pieces aren't parts of the item (`imported_meshes`: an outfit's armatures are merged
    and its pose assets applied over the parts only). A socket the item's mesh doesn't have (a
    pickaxe's `idle_fx` where the mesh has `FX_Idle`) leaves the effect on the mesh's origin, as the
    engine's attachment does. Not reproduced: a material that reads the scene behind it
    (`SceneTexture`: an aura drawn on the character's own normals). Test: `fork-export-asset?type=Glider&effects=1`,
    `fork-asset-page?type=Glider&name=...&pick=Effects:Its trail&export=1`.
  - **An animation's effects.** An emote's (or any exported animation's) Niagara notifies come with
    it (`AnimExport.MPEffects`, `EffectNotify`: a notify or timed notify whose `Template` is a
    Niagara system - the montage's own and its sequences'). One effect per system and socket, with
    the times of all its notifies and how long each timed one lasts. The plugin
    (`effects.from_animation`) puts each on the animated armature, on its socket with the notify's
    offsets, and replays it once per notify from that notify's frame (`mp_effect_repeats`,
    `mp_effect_lengths`; the plays are merged into one set of particles). A timed notify's end asks
    the system to stop (`System.deactivate`: Engine.Owner.ExecutionState): it spawns no more and
    plays out. The skeleton's sockets come along (`MPSockets`): an armature without them still
    places an effect on a socket (on the socket's bone, where the skeleton puts it) and answers the
    scripts that read one.
  - **A swing's trails.** A pickaxe swing animation (a harvesting montage, from the Files tab) turns
    the held pickaxe's trails on and off with notifies (`FortAnimNotify_MeleeAnimTrails_On` /
    `_Off`); they are exported as windows (`AnimExport.MPTrails`; a notify's time is its own link's:
    `GetTime`). Imported onto a character, the windows go to the trail and swing effects of the
    pickaxe under its armature (`effects.swing`; with none there, to every pickaxe trail in the
    scene), which are replayed once per swing. The held pickaxe's idle effect is replayed too (its
    world-space particles were left where the pickaxe was when it was imported).
  - **Ribbons.** A ribbon's width runs across the view, the particles' facing, or along their side
    vector (a trail between two sockets), as the renderer says (the modifier's Facing). Its two UV
    sets are laid along it as the renderer's `UV0Settings` / `UV1Settings` say (`_ribbon_uv`: over
    the whole ribbon by length or evenly a point, or tiled every Tiling Length from its leading
    edge or by each particle's `RibbonUVDistance`; scale, offset, the emitter's own U and V range),
    from its first point in link order (the youngest, without one); each point carries them
    (`mp_uv0`, `mp_uv1`: U, V at one edge, V at the other). Its shape is a plane, several planes
    turned about it (`MultiPlaneCount`) or a tube (`TubeSubdivisions`): the modifier's Shape, Sides.
  - **Sprites and meshes on their particle.** A sprite's pivot (`PivotInUVSpace`: a flame whose
    base, not its middle, is the particle) and a mesh's `PivotOffset` are the modifier's Piece
    Offset; a sprite with Custom Alignment runs its length along the emitter's own vector
    (`SpriteAlignment`: `mp_align`, which is the velocity otherwise). A sprite's or a mesh's
    `CameraOffset` draws it that far toward the camera (`mp_camera_offset`: a glow out in front of
    the smoke it sits in).
  - **Decals.** A decal renderer's particle is a quad across its box (its local YZ plane, the box's
    half size `DecalSize` over it, turned by `DecalOrientation`: the engine's own turn, straight
    down, where the emitter sets none), both sides drawn; its material's Decal Color is the
    particle's `DecalColor`, its Decal Lifetime Opacity the particle's `DecalFade`
    (`mp_decal_fade`). The scene it projects onto isn't here: a ground decal lies flat.
  - **Lights.** A light renderer's particles are point lights, one a particle alive at once (up to
    32), keyed frame by frame (`_lights`): where each is, its colour (`Color`, alpha scaling it if the
    renderer says, plus `ColorAdd`), its reach (`LightRadius` x `RadiusScale`: the light's custom
    distance) and its power - what UE's light gives a surface a third of the way out (exponent
    falloff), or anywhere (inverse square: UE's particle light colour is per cm²). Diffuse and
    specular scales carry over. A component renderer's lights aren't drawn.
  - **Looping.** An effect loops by default (its empty's `Loop`, the modifiers' Loop, the lights'
    curves cycling): one timed by an animation, or played on a swing or an event, doesn't. Clear
    `Loop` and Replay Effect to play it once.
  - **Which renderers draw.** A renderer bound to a visibility tag draws only the particles whose
    tag is its `RendererVisibility` (one emitter, several looks); one whose `RendererEnabledBinding`
    reads false draws nothing; a piece with no particles of its own is hidden.
  - A renderer's material kept inside the system (an instance with the renderer's parameters) is
    exported as the asset it is an instance of, with its values.
  - **What the engine does around the scripts** (found by replaying 3,000 of the game's systems
    and checking every emitter's particles, then rendering 200 effects and items and measuring
    each played piece - `ns_audit.py`, `fx_audit.py` in the session's scratchpad):
    - the VM's division, roots, powers and logarithms are the engine's safe ones (a division by
      nearly nothing gives 0, not an infinity that spreads over a particle's position);
    - an event spawns its Spawn Number of particles whatever its handler's script then runs on;
    - half-precision attributes (`NiagaraHalf...`: an emitter's compressed ones) have their own
      rows; a user-defined struct in a data set (a Fortnite module's bone data) is laid out as the
      app says (`MPStruct`: its floats and ints, `Effects.Components`);
    - the parameter collections a script reads take their collection's own values (`MPCollection`:
      the default instance's store; `NPC.FortniteNPC.FortniteActiveTimeOfDay` is Day - a script's
      cooked store holds every time of day at once, which made 4 in 10 systems' tints several
      times too bright and lit what only shows at night). They are properties on the effect's
      empty beside its user parameters: set the time of day to `[0, 0, 0, 1]` (morning, day,
      evening, night) and Replay Effect for the night's look;
    - the system's own scripts answer zeros for what can't be answered here (a grid a GPU emitter
      simulates on, a data channel) instead of the whole system failing (`System.unanswered`);
    - an array a script fills as it plays is as wide as its type; a weighted distribution array
      gives its alias table; a mesh renderer's `GetMeshLocalBounds` (`MPBounds`), the camera's
      field of view;
    - a mesh renderer's override material also goes to a slot the mesh itself leaves empty;
    - UE's names don't mind case: an effect's `User.bisFullyDeployed` is the `User.bIsFullyDeployed`
      the export sets.
  - **A static mesh to sample** (its surface, vertices or sockets): the engine samples the mesh of
    the component the effect is on, which doesn't come with the effect. Its functions answer as the
    engine's do with no mesh (`niagara.NoMesh`): counts 0, positions 0 - the particles start at the
    effect's origin - a socket's rotation none, its scale 1. An emitter that spawns from the mesh's
    triangles spawns nothing, as in the game without one.
  - When an emitter spawns nothing, the log names the system's user parameters still at 0 (a
    burst's count the game sets: `User.Burst`): set one on the effect's empty and Replay Effect.
  - **Not replayed:** GPU emitters (7% of the game's own emitters, 4 of 5 of the islands') keep
    only a compiled shader; their pieces stay as imported, in a row beside the effect. Without an
    armature, a script that reads a character's bones or sockets finds them all at the effect's
    origin. Collisions find nothing to hit. An emitter that samples a skinned mesh's surface,
    water, or reads a data channel is left out. A material that reads the scene behind it (a
    particle decal, a refraction) draws nothing. Component renderers (lights, post process) draw
    nothing.
  - Of 2,000 of the game's own systems sampled, all but 3 (uncooked templates) play; of their
    7,330 emitters 498 are GPU and 66 are left out for what they read.
    Test route: `fork-effect-program?path=` (what the export carries for the replay).
  Build revision 12: an additive material's light is Emissive * Opacity (it was Emissive alone: a
  flash drew as its whole quad). 13: particle values from the instance, a sprite's sub-image.
  17: a particle material's World Position Offset, UE's division by zero. 18: a SmoothStep over an
  empty range, Particle Random from the particle. 19: a Niagara decal's colour and fade.
- **Rocket Racing cars.** Assets > Rocket Racing > Cars lists the car bodies.
  Styles (Tier, Body Color, Painted, Decal, Decal Color, Wheels) come from
  Material Porter's car assembly (`Exporting/MaterialPorter/Cars.cs`); the
  export is the body (the decal's material when one is picked), the wheels
  as children on its wheel sockets, and the values the car's Mutable program
  gives each material (`Mutable.g.cs`), sent to Blender as a Vehicle.
  A newer car's tier names no skeletal mesh (39 of the 120 bodies, the Pizza
  Planet truck among them), nor do some wheels: the program builds them.
  `MutableProgram.MeshSurfaces` follows the component (the item's
  `ComponentIndex`) to its surfaces op, whose LOD 0 list names each surface's
  constant mesh and, by surface id, its material; `MutableMeshes.g.cs` decodes
  the constants onto the tier's skeleton (a wheel onto `SK_Wheel_Base_Skeleton`),
  written once per build as `Assets/MaterialPorter/Cars/<item>_<hash>.uemodel`.
  A decal's own values (a body's skin switch: its trim, chassis and interior
  materials) go into the program too, as do the item's values for the painted
  row picked (`AdditionalVariantInfos`, keyed by the row's
  `Cosmetics.Variant.Property.Vehicle.Painted.<row>` tag: the Patty Wagon's
  "None" row keeps its burger's textures), the wheel's own default painted row,
  and the windows of the tier's mode-less `WindowQueryInfos` entry.
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
  (`JunoBuilding{Prop,Set}AccountItemDefinition`), the meshes of the actor each previews;
  everything LEGO Fortnite builds (`JunoBuildInstructionsItemDefinition`, about 9,300: walls,
  floors, roofs, doors, furniture as JBID_, crafting stations, chests as PBID_), its DataList's
  `ActorClass`; and cave rooms (`PDA_Juno_ProcCave_ShellData_C`), their level. A filter
  category (Props & Sets, Building Pieces, Stations & Placeables, Caves) splits them; a build
  without a display name shows its asset's, and its description its theme and size. LEGO
  builds are Geometry Collections (bricks that break apart): `ExportContext.GeometryCollection.cs`
  draws one whole from its collection's root proxy meshes (the piece and its "_CP" common parts),
  for SCS components and a class's native ones (a chest's).
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
  single-file build), `ExportContext.Unreal.cs` and `MeshExport.cs` (Geometry Collections, LEGO
  props), `ExportContext.Fortnite.cs` (weapon looks), `Enums.cs` (the fork's export types), `AssetInfo.cs` and
  `ExportService.cs` (the fork's style lists), `FortnitePorting.csproj`, `build-release.yml`, `build-commit.yml`, `README.md`, plugin
  `server.py`, `material_context.py`, `mesh_context.py`, `importer.py` and `enums.py`.

Generated files come from Material Porter (`Documents/Claude/materialporter`):
edit there, then `python tools/sync_fork.py`.

- `patches/CUE4Parse/`: whole files laid over the `external/CUE4Parse`
  submodule (upstream's, which the fork can't push to). Both workflows copy them
  in after the submodule checkout; locally, copy them into the submodule the
  same way. Today: `FSpline.cs`, which reads UE 5.6's new spline format
  (positions, then Rotation/Scale attribute channels: control values, knots,
  interp modes) where upstream throws, so SplineComponents and
  WaterSplineComponents lost everything after it. After updating the submodule,
  check whether upstream now reads it and drop the overlay if so.

## Merging upstream

    git fetch upstream
    git merge upstream/main

Conflicts can only come from the small edits above.
