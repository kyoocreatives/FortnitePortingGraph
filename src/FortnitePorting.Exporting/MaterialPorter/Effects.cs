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
            exports.Add(new JObject { ["name"] = export.Name, ["type"] = export.ExportType, ["outer"] = export.Outer?.Name.Text, ["props"] = props });
        }
        return exports;
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
