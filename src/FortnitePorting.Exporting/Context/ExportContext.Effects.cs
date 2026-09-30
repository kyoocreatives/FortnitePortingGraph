using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using FortnitePorting.Exporting.MaterialPorter;
using FortnitePorting.Exporting.Models;

namespace FortnitePorting.Exporting.Context;

/// <summary>
/// Material Porter fork: a particle effect (a Niagara system) as what it is made of. Each enabled
/// emitter becomes an empty, and under it what its renderers draw: a mesh renderer's meshes with
/// the materials it puts on them, a sprite or ribbon renderer's material on a plane the plugin
/// makes. A renderer's own material parameters ride on the material (MPValues). How the particles
/// move isn't here: a GPU emitter keeps only its compiled shader, a CPU emitter its compiled
/// script (Effects.Describe says which each is).
/// </summary>
public partial class ExportContext
{
    public ExportMesh Effect(UObject system)
    {
        var root = new ExportMesh { Name = system.Name, IsEmpty = true };
        var at = 0;
        foreach (var emitter in Effects.Emitters(system))
        {
            // laid out in a row, 2 m apart: a palette to pick from (in the game they all sit at the system's origin)
            var node = new MaterialPorterMesh
            {
                Name = emitter.Name, IsEmpty = true,
                Location = new FVector(0, 200 * at++, 0),
                MPEffect = new Dictionary<string, object> { ["Kind"] = "Emitter", ["Sim"] = emitter.Sim },
            };
            foreach (var renderer in emitter.Renderers)
            {
                try
                {
                    node.Children.AddRange(EffectRenderer(emitter, renderer));
                }
                catch (Exception e)
                {
                    Serilog.Log.Warning("[Material Porter] {System}: {Emitter}'s {Renderer} wasn't read ({Error})",
                        system.Name, emitter.Name, renderer.ExportType, e.Message);
                }
            }
            root.Children.Add(node);
        }
        return root;
    }

    /// <summary>A renderer's material parameters (scalars, vectors, textures it sets on its materials), or null.</summary>
    private static ParamSet? RendererValues(UObject renderer)
    {
        if (!renderer.TryGetValue(out FStructFallback parameters, "MaterialParameters")) return null;
        var values = new ParamSet();
        foreach (var p in parameters.GetOrDefault("ScalarParameters", Array.Empty<FStructFallback>()))
            values.Scalars[p.GetOrDefault<FName>("MaterialParameterName").Text] = p.GetOrDefault<float>("Value");
        foreach (var p in parameters.GetOrDefault("VectorParameters", Array.Empty<FStructFallback>()))
        {
            var c = p.GetOrDefault<FLinearColor>("Value");
            values.Vectors[p.GetOrDefault<FName>("MaterialParameterName").Text] = [c.R, c.G, c.B, c.A];
        }
        foreach (var p in parameters.GetOrDefault("TextureParameters", Array.Empty<FStructFallback>()))
            if (p.GetOrDefault<UTexture?>("Texture") is { } texture)
                values.Textures[p.GetOrDefault<FName>("MaterialParameterName").Text] = texture.GetPathName();
        return values.Scalars.Count + values.Vectors.Count + values.Textures.Count > 0 ? values : null;
    }

    private ExportMaterial? EffectMaterial(UMaterialInterface? material, int slot, ParamSet? values)
    {
        if (material is null || Material(material, slot) is not { } export) return null;
        if (values is null) return export;
        return new MaterialPorterMaterial(export) { MPValues = values, Hash = HashCode.Combine(export.Hash, values.Key()) };
    }

    private IEnumerable<ExportMesh> EffectRenderer(Effects.Emitter emitter, UObject renderer)
    {
        var values = RendererValues(renderer);
        switch (renderer.ExportType)
        {
            case "NiagaraMeshRendererProperties":
            {
                var overrides = renderer.GetOrDefault("bOverrideMaterials", false)
                    ? renderer.GetOrDefault("OverrideMaterials", Array.Empty<FStructFallback>())
                    : [];
                foreach (var entry in renderer.GetOrDefault("Meshes", Array.Empty<FStructFallback>()))
                {
                    if (entry.GetOrDefault<UStaticMesh?>("Mesh") is not { } staticMesh || Mesh(staticMesh) is not { } mesh) continue;
                    var export = new MaterialPorterMesh(mesh)
                    {
                        Scale = entry.GetOrDefault("Scale", FVector.OneVector),
                        Rotation = entry.GetOrDefault("Rotation", FRotator.ZeroRotator),
                        MPEffect = new Dictionary<string, object> { ["Kind"] = "Mesh" },
                    };
                    // each slot: the renderer's override, else the mesh's own, with the renderer's parameters
                    foreach (var slot in mesh.Materials.Select(m => m.Slot).Distinct())
                    {
                        var material = slot < overrides.Length ? overrides[slot].GetOrDefault<UMaterialInterface?>("ExplicitMat") : null;
                        if (material is not null)
                        {
                            if (EffectMaterial(material, slot, values) is { } over) export.OverrideMaterials.Add(over);
                        }
                        else if (values is not null && mesh.Materials.FirstOrDefault(m => m.Slot == slot) is { } own)
                        {
                            export.OverrideMaterials.Add(new MaterialPorterMaterial(own) { MPValues = values, Hash = HashCode.Combine(own.Hash, values.Key()) });
                        }
                    }
                    yield return export;
                }
                break;
            }
            case "NiagaraSpriteRendererProperties" or "NiagaraRibbonRendererProperties":
            {
                if (EffectMaterial(renderer.GetOrDefault<UMaterialInterface?>("Material"), 0, values) is not { } material) break;
                var sub = renderer.GetOrDefault("SubImageSize", new FVector2D(1, 1));
                yield return new MaterialPorterMesh
                {
                    Name = $"{emitter.Name} {(renderer.ExportType.Contains("Ribbon") ? "ribbon" : "sprite")}",
                    IsEmpty = true,
                    MPEffect = new Dictionary<string, object>
                    {
                        ["Kind"] = renderer.ExportType.Contains("Ribbon") ? "Ribbon" : "Sprite",
                        ["Material"] = material,
                        ["SubImages"] = new[] { sub.X, sub.Y },
                        ["Facing"] = renderer.GetOrDefault<FName>("FacingMode").Text.Split("::").Last(),
                    },
                };
                break;
            }
        }
    }
}
