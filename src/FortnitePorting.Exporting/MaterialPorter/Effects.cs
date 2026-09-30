using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Engine.VectorField;
using CUE4Parse.UE4.Objects.UObject;
using FortnitePorting.CUE4Parse.Extensions;
using CUE4Parse.UE4.Objects.Engine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>
/// Material Porter fork: a Niagara system's emitters as the cooked asset keeps them. A system
/// lists emitter handles; a handle is a standard emitter (one of its versions: CPU or GPU
/// simulated, its renderers) or a stateless one (UE 5.4's lightweight emitters: plain settings).
/// </summary>
public static class Effects
{
    public const string ContrailClass = "AthenaSkyDiveContrailItemDefinition";
    /// <summary>A contrail item's effect (older ones name only a Cascade effect: none to replay).</summary>
    public const string ContrailEffect = "NiagaraContrailEffect";

    /// <summary>
    /// A pickaxe's own effects, as its weapon definition's data names them: the property, what it is
    /// called, and the property naming the socket it sits on (the trail runs between two sockets instead).
    /// </summary>
    public static readonly (string Property, string Name, string? Socket)[] PickaxeEffects =
    [
        ("AnimTrailsNiagara", "trail", null),
        ("SwingEffectNiagara", "swing", "SwingFXSocketName"),
        ("IdleEffectNiagara", "idle", "IdleFXSocketName"),
    ];
    public const string TrailFirstSocket = "AnimTrailsFirstSocketName", TrailSecondSocket = "AnimTrailsSecondSocketName";

    /// <summary>Which of its own effects a pickaxe's weapon definition has: "trail", "swing", "idle".</summary>
    public static List<string> PickaxeEffectNames(UObject weaponDefinition) =>
        PickaxeEffects.Where(e => Named(weaponDefinition.GetDataListItem<FSoftObjectPath>(e.Property).AssetPathName)).Select(e => e.Name).ToList();

    public const string PartEffect = "IdleEffectNiagara", PartSocket = "IdleFXSocketName";

    /// <summary>
    /// The system a soft path names, where it shows something: many parts name a blank system
    /// (NS_Blank_Body, NS_Empty: no emitter) to switch their base part's effect off.
    /// </summary>
    public static UObject? Shown(FSoftObjectPath path)
    {
        try
        {
            return Named(path.AssetPathName) && path.TryLoad(out UObject? system) && Emitters(system).Count > 0 ? system : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>An outfit's character parts: its own, else its hero definition's first specialization's.</summary>
    public static UObject[] OutfitParts(UObject outfit)
    {
        var parts = outfit.GetOrDefault("BaseCharacterParts", Array.Empty<UObject>());
        if (parts.Length == 0 && outfit.TryGetValue(out UObject hero, "HeroDefinition") && hero.TryGetValue(out UObject[] specializations, "Specializations"))
            parts = specializations.FirstOrDefault()?.GetOrDefault("CharacterParts", Array.Empty<UObject>()) ?? [];
        return parts;
    }

    /// <summary>A glider's trail effects: (system, socket, offset), from its trail definitions or its older trail properties.</summary>
    public static List<(FSoftObjectPath System, FName Socket, FTransform? Offset)> GliderTrails(UObject glider)
    {
        var trails = new List<(FSoftObjectPath, FName, FTransform?)>();
        foreach (var trail in glider.GetOrDefault("TrailEffectDefinitions", Array.Empty<FStructFallback>()))
        {
            var system = trail.GetOrDefault<FSoftObjectPath>("NiagaraSystem");
            if (Named(system.AssetPathName)) trails.Add((system, trail.GetOrDefault<FName>("EffectSocket"), trail.GetOrDefault<FTransform?>("Offset")));
        }
        if (trails.Count == 0)
            foreach (var property in new[] { "TrailEffectNiagara", "TrailEffectNiagara2" })
            {
                var system = glider.GetOrDefault<FSoftObjectPath>(property);
                if (Named(system.AssetPathName) && trails.All(t => t.Item1.AssetPathName != system.AssetPathName)) trails.Add((system, default, null));
            }
        return trails;
    }

    /// <summary>
    /// A weapon actor class's Niagara components, its own and its parent classes': (component, socket
    /// on the weapon it is attached to, whether it plays by itself rather than on an event of the game's).
    /// </summary>
    public static List<(UObject Component, string? Socket, bool Auto)> WeaponComponents(UObject? actorClass)
    {
        var found = new List<(UObject, string?, bool)>();
        for (var guard = 0; actorClass is UBlueprintGeneratedClass && guard < 6; guard++)
        {
            var exports = actorClass.Owner?.GetExports().ToList() ?? [];
            foreach (var component in exports.Where(e => e.ExportType == "NiagaraComponent"))
            {
                if (!Named(component.GetOrDefault<FPackageIndex?>("Asset")?.Name is { } asset ? new FName(asset) : default)) continue;
                if (found.Any(f => f.Item1.Name == component.Name)) continue;
                // the construction script node that owns the component says what it is attached to
                var node = exports.FirstOrDefault(e => e.ExportType == "SCS_Node" && e.GetOrDefault<FPackageIndex?>("ComponentTemplate")?.Name == component.Name);
                var socket = node?.GetOrDefault<FName>("AttachToName") ?? default;
                found.Add((component, Named(socket) ? socket.Text : null, component.GetOrDefault("bAutoActivate", true)));
            }
            actorClass = (actorClass as UBlueprintGeneratedClass)?.SuperStruct?.Load<UObject>();
        }
        return found;
    }

    /// <summary>
    /// What of its own effects an item of a tab can be exported with ("trail", "swing", "idle",
    /// "event effects"): a pickaxe's weapon definition's, a back bling's or an outfit's parts' idle
    /// effects, a glider's trails, a weapon's actor class's Niagara components.
    /// </summary>
    public static List<string> OwnEffectNames(UObject item, EExportType type)
    {
        switch (type)
        {
            case EExportType.Pickaxe:
                return item.GetOrDefault<UObject?>("WeaponDefinition") is { } weapon ? PickaxeEffectNames(weapon) : [];
            case EExportType.Backpack or EExportType.Outfit:
                var parts = type is EExportType.Backpack ? item.GetOrDefault("CharacterParts", Array.Empty<UObject>()) : OutfitParts(item);
                return parts.Any(p => Shown(p.GetOrDefault<FSoftObjectPath>(PartEffect)) is not null) ? ["idle"] : [];
            case EExportType.Glider:
                return GliderTrails(item).Any(t => Shown(t.System) is not null) ? ["trail"] : [];
            case EExportType.Item:
                var components = WeaponComponents(item.GetOrDefault<UObject?>("WeaponActorClass") ?? item.GetDataListItem<UObject?>("WeaponActorClass"));
                var names = new List<string>();
                if (components.Any(c => c.Auto)) names.Add("idle");
                if (components.Any(c => !c.Auto)) names.Add("event effects");
                return names;
            default:
                return [];
        }
    }

    /// <summary>Whether a name names something (it is set, and isn't None).</summary>
    public static bool Named(FName name) => name.Text is { Length: > 0 } text && text != "None";

    /// <summary>An enabled emitter: its name, how it is simulated (CPU, GPU, Stateless) and its enabled renderers.</summary>
    public sealed record Emitter(string Name, string Sim, UObject Asset, FStructFallback? Version, List<UObject> Renderers);

    public static List<Emitter> Emitters(UObject system)
    {
        var emitters = new List<Emitter>();
        foreach (var handle in system.GetOrDefault("EmitterHandles", Array.Empty<FStructFallback>()))
        {
            if (!handle.GetOrDefault("bIsEnabled", true)) continue;
            var name = handle.GetOrDefault<FName>("Name").Text;
            if (handle.GetOrDefault<UObject?>("StatelessEmitter") is { } stateless)
            {
                emitters.Add(new Emitter(name, "Stateless", stateless, null, Enabled(stateless.GetOrDefault("RendererProperties", Array.Empty<UObject>()))));
                continue;
            }
            if (handle.GetOrDefault<FStructFallback?>("VersionedInstance") is not { } instance
                || instance.GetOrDefault<UObject?>("Emitter") is not { } emitter) continue;
            var versions = emitter.GetOrDefault("VersionData", Array.Empty<FStructFallback>());
            if (versions.Length == 0) continue;
            // the handle's version of the emitter, else its only one
            var wanted = instance.GetOrDefault<FGuid>("Version");
            var version = versions.FirstOrDefault(v => v.GetOrDefault<FStructFallback?>("Version")?.GetOrDefault<FGuid>("VersionGuid") == wanted) ?? versions[0];
            var sim = version.GetOrDefault<FName>("SimTarget").Text.Contains("GPU") ? "GPU" : "CPU";
            emitters.Add(new Emitter(name, sim, emitter, version, Enabled(version.GetOrDefault("RendererProperties", Array.Empty<UObject>()))));
        }
        return emitters;
    }

    // compiled data only the engine's own VM and the editor read
    private static readonly HashSet<string> Unread =
        ["ExperimentalContextData", "StatScopes", "CompileTags", "ShaderScriptParametersMetadata", "SimulationStageMetaData"];

    /// <summary>
    /// What a replay of the system is made from: its package's exports (name, type, outer,
    /// properties) in the package's order, which holds each CPU emitter's compiled scripts, their
    /// parameters and curves. The plugin runs them (material_porter/niagara.py).
    /// </summary>
    public static JArray Program(UObject system)
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
        var exports = new JArray();
        foreach (var export in system.Owner!.GetExports())
        {
            var props = new JObject();
            foreach (var p in export.Properties)
                if (p.Tag?.GenericValue is { } value && !props.ContainsKey(p.Name.Text))
                    props[p.Name.Text] = JToken.FromObject(value, serializer);
            Prune(props);
            if (export.ExportType == "NiagaraMeshRendererProperties" && MeshBounds(export) is { } bounds) props["MPBounds"] = bounds;
            exports.Add(new JObject { ["name"] = export.Name, ["type"] = export.ExportType, ["outer"] = export.Outer?.Name.Text, ["props"] = props });
        }
        // the parameter collections its scripts read (the game's time of day, wind): each one's own
        // values (its default instance's store). A script's cooked store only holds placeholders.
        var collections = new Dictionary<string, UObject>();
        foreach (var export in system.Owner!.GetExports())
            if (export.ExportType == "NiagaraScript")
                foreach (var collection in export.GetOrDefault("CachedParameterCollectionReferences", Array.Empty<UObject>()))
                    collections.TryAdd(collection.GetPathName(), collection);
        foreach (var collection in collections.Values)
        {
            try
            {
                if (collection.GetOrDefault<UObject?>("DefaultInstance") is not { } instance) continue;
                var props = new JObject();
                foreach (var p in instance.Properties)
                    if (p.Tag?.GenericValue is { } value && !props.ContainsKey(p.Name.Text))
                        props[p.Name.Text] = JToken.FromObject(value, serializer);
                exports.Add(new JObject { ["name"] = collection.Name, ["type"] = "MPCollection", ["outer"] = null, ["props"] = props });
            }
            catch (Exception e)
            {
                Serilog.Log.Warning("[Material Porter] {System}: the parameter collection {Collection} wasn't read ({Error})", system.Name, collection.Name, e.Message);
            }
        }
        // the user-defined structs its data sets hold (a Fortnite module's bone data): how many
        // floats and ints each is laid out as, which the asset itself doesn't say
        var structs = new Dictionary<string, FPackageIndex>();
        foreach (var export in system.Owner!.GetExports())
            foreach (var p in export.Properties)
                FindStructs(p.Tag?.GenericValue, structs, 0);
        foreach (var (name, index) in structs)
        {
            try
            {
                if (index.Load<UStruct>() is not { } type) continue;
                var (floats, ints) = Components(type, 0);
                exports.Add(new JObject { ["name"] = name, ["type"] = "MPStruct", ["outer"] = null, ["props"] = new JObject { ["Floats"] = floats, ["Ints"] = ints } });
            }
            catch (Exception e)
            {
                Serilog.Log.Warning("[Material Porter] {System}: the struct {Struct} wasn't read ({Error})", system.Name, name, e.Message);
            }
        }
        return exports;
    }

    /// <summary>
    /// A mesh renderer's meshes' bounds, each with the renderer's scale of it (what a script's
    /// GetMeshLocalBounds reads): min then max, six numbers a mesh.
    /// </summary>
    private static JArray? MeshBounds(UObject renderer)
    {
        try
        {
            var all = new JArray();
            foreach (var entry in renderer.GetOrDefault("Meshes", Array.Empty<FStructFallback>()))
            {
                var scale = entry.GetOrDefault("Scale", FVector.OneVector);
                var box = entry.GetOrDefault<global::CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh?>("Mesh")?.RenderData?.Bounds;
                var (min, max) = box is { } b ? (b.Origin - b.BoxExtent, b.Origin + b.BoxExtent) : (FVector.ZeroVector, FVector.ZeroVector);
                all.Add(new JArray(min.X * scale.X, min.Y * scale.Y, min.Z * scale.Z, max.X * scale.X, max.Y * scale.Y, max.Z * scale.Z));
            }
            return all.Count > 0 ? all : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The user-defined structs named by the type definitions (FNiagaraTypeDefinition.ClassStructOrEnum) under a value.</summary>
    private static void FindStructs(object? value, Dictionary<string, FPackageIndex> found, int depth)
    {
        if (depth > 12) return;
        switch (value)
        {
            case FStructFallback fallback:
                foreach (var p in fallback.Properties)
                {
                    if (p.Name.Text == "ClassStructOrEnum" && p.Tag?.GenericValue is FPackageIndex { IsNull: false } index)
                    {
                        if (index.ResolvedObject?.Class?.Name.Text == "UserDefinedStruct") found.TryAdd(index.Name, index);
                        continue;
                    }
                    FindStructs(p.Tag?.GenericValue, found, depth + 1);
                }
                break;
            case FScriptStruct { StructType: FStructFallback inner }:
                FindStructs(inner, found, depth + 1);
                break;
            case FScriptStruct { StructType: global::CUE4Parse.UE4.Objects.Niagara.FNiagaraVariableBase variable }:
                FindStructs(variable.TypeDef, found, depth + 1);        // (a data set's variable: its name and type)
                break;
            case UScriptArray array:
                // (arrays of numbers - bytecode, parameter data - hold no type definition)
                if (array.InnerType is "StructProperty")
                    foreach (var item in array.Properties)
                        FindStructs(item.GenericValue, found, depth + 1);
                break;
        }
    }

    /// <summary>A struct as a Niagara data set lays it out: its fields' floats, then ints (a vector: three floats, as Niagara narrows it).</summary>
    private static (int Floats, int Ints) Components(UStruct type, int depth)
    {
        int floats = 0, ints = 0;
        foreach (var field in type.ChildProperties ?? [])
        {
            switch (field)
            {
                case FStructProperty inner:
                    var known = inner.Struct.Name switch
                    {
                        "Vector" or "Vector3f" or "Vector3d" or "Rotator" => 3,
                        "Vector2D" or "Vector2f" => 2,
                        "Vector4" or "Vector4f" or "Quat" or "Quat4f" or "LinearColor" => 4,
                        _ => 0,
                    };
                    if (known > 0) floats += known;
                    else if (depth < 4 && inner.Struct.Load<UStruct>() is { } nested)
                    {
                        var (f, i) = Components(nested, depth + 1);
                        floats += f;
                        ints += i;
                    }
                    break;
                case FFloatProperty or FDoubleProperty:
                    floats++;
                    break;
                case FBoolProperty or FEnumProperty or FNumericProperty:
                    ints++;
                    break;
            }
        }
        return (floats, ints);
    }

    /// <summary>
    /// The vector fields the system's scripts sample (a vector field data interface's Field), by
    /// package: its grid size, bounds and vectors (four half floats a cell, as the asset keeps them).
    /// </summary>
    public static JObject Fields(UObject system)
    {
        var fields = new JObject();
        foreach (var export in system.Owner!.GetExports())
        {
            if (export.ExportType != "NiagaraDataInterfaceVectorField") continue;
            try
            {
                if (export.GetOrDefault<UVectorFieldStatic?>("Field") is not { } field || field.Owner is not { } package) continue;
                if (fields.ContainsKey(package.Name) || field.SourceData?.Data is not { Length: > 0 } data) continue;
                var bounds = field.GetOrDefault<FBox>("Bounds");
                fields[package.Name] = new JObject
                {
                    ["Size"] = new JArray(field.GetOrDefault<int>("SizeX"), field.GetOrDefault<int>("SizeY"), field.GetOrDefault<int>("SizeZ")),
                    ["Min"] = new JArray(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
                    ["Max"] = new JArray(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
                    ["Data"] = Convert.ToBase64String(data),
                };
            }
            catch (Exception e)
            {
                Serilog.Log.Warning("[Material Porter] {System}: {Field}'s vector field wasn't read ({Error})", system.Name, export.Name, e.Message);
            }
        }
        return fields;
    }

    private static void Prune(JToken token)
    {
        if (token is JObject o)
        {
            foreach (var name in o.Properties().Select(p => p.Name).Where(Unread.Contains).ToList()) o.Remove(name);
            foreach (var p in o.Properties()) Prune(p.Value);
        }
        else if (token is JArray a && a.Count > 0 && a[0] is JContainer)
        {
            foreach (var item in a) Prune(item);
        }
    }

    private static List<UObject> Enabled(IEnumerable<UObject?> renderers) =>
        renderers.Where(r => r is not null && r.GetOrDefault("bIsEnabled", true)).Select(r => r!).ToList();

    private static string Draws(UObject renderer) => renderer.ExportType switch
    {
        "NiagaraMeshRendererProperties" => "mesh",
        "NiagaraSpriteRendererProperties" => "sprite",
        "NiagaraRibbonRendererProperties" => "ribbon",
        "NiagaraLightRendererProperties" => "light",
        var other => other.Replace("Niagara", "").Replace("RendererProperties", "").ToLowerInvariant()
    };

    /// <summary>A system in a line: "3 emitters: Rays (mesh, CPU), Flare (sprite, stateless), Sparks (sprite, GPU)".</summary>
    public static string Describe(UObject system)
    {
        try
        {
            var emitters = Emitters(system);
            if (emitters.Count == 0) return "No emitters.";
            var parts = emitters.Select(e =>
            {
                var draws = e.Renderers.Select(Draws).Distinct().ToList();
                return $"{e.Name} ({(draws.Count == 0 ? "draws nothing" : string.Join(" + ", draws))}, {(e.Sim == "Stateless" ? "stateless" : e.Sim)})";
            });
            return $"{emitters.Count} emitter{(emitters.Count == 1 ? "" : "s")}: {string.Join(", ", parts)}";
        }
        catch
        {
            return "";
        }
    }
}
