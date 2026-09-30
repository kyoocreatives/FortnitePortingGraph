using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>
/// Material Porter fork: a Niagara system's emitters as the cooked asset keeps them. A system
/// lists emitter handles; a handle is a standard emitter (one of its versions: CPU or GPU
/// simulated, its renderers) or a stateless one (UE 5.4's lightweight emitters: plain settings).
/// </summary>
public static class Effects
{
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
