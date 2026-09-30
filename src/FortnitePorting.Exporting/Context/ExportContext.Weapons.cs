using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using FortnitePorting.Exporting.MaterialPorter;
using FortnitePorting.Exporting.Models;
using FortnitePorting.Shared.Extensions;

namespace FortnitePorting.Exporting.Context;

/// <summary>
/// Material Porter fork: a weapon as the game draws it. FP exports the item's mesh with that mesh's
/// own materials, but many weapons get their look elsewhere:
/// - the weapon mesh component of its actor class: material overrides and custom primitive data
///   (a Morphite weapon's crystal material, on the same mesh as the plain weapon);
/// - a wrap: the item's own (IntrinsicOverrideWrap: an exotic's glow).
/// A wrap is one material instance of M_FN_Customization_MASTER. That master only carries the
/// customization layer (MF_Base_Customization) every weapon master runs too, over a dummy base:
/// the game lays the wrap's values over the weapon's own material, which keeps its own textures
/// (diffuse, normals, masks, its customization mask). The exact materials do the same: the
/// weapon's material with the wrap's scalars, vectors, textures and static switches over it.
/// </summary>
public partial class ExportContext
{
    /// <summary>An object, then the archetypes it inherits from (a child class's defaults, its parent's...).</summary>
    private static IEnumerable<UObject> Archetypes(UObject? o)
    {
        for (var guard = 0; o != null && guard < 8; guard++)
        {
            yield return o;
            o = o.Template?.Load();
        }
    }

    /// <summary>The weapon's meshes (WeaponDefinition's) as its actor class and its own wrap show them.</summary>
    public void WeaponLook(UObject weaponDefinition, List<ExportMesh> meshes)
    {
        if (meshes.Count == 0) return;
        try
        {
            if (weaponDefinition.TryGetValue(out UBlueprintGeneratedClass actorClass, "WeaponActorClass")
                && actorClass.ClassDefaultObject?.Load() is { } defaults)
            {
                WeaponComponentLook(defaults, "WeaponMesh", meshes, 0);
                WeaponComponentLook(defaults, "LeftHandWeaponMesh", meshes, 1);
            }

            if (weaponDefinition.GetOrDefault<FSoftObjectPath>("IntrinsicOverrideWrap").TryLoad(out UObject? wrap)
                && WrapValues(wrap) is { } values)
            {
                foreach (var mesh in meshes) ApplyWrap(mesh, values);
            }
        }
        catch (Exception e)
        {
            // the plain weapon, then
            Serilog.Log.Warning("[Material Porter] {Weapon}: its look wasn't read ({Error})", weaponDefinition.Name, e.Message);
        }
    }

    /// <summary>A weapon mesh component's material overrides and custom primitive data, onto the mesh it shows.</summary>
    private void WeaponComponentLook(UObject defaults, string property, List<ExportMesh> meshes, int index)
    {
        if (index >= meshes.Count) return;
        UObject? component = null;
        foreach (var d in Archetypes(defaults))
        {
            if (!d.TryGetValue(out UObject found, property)) continue;
            component = found;
            break;
        }
        if (component is null) return;

        var mesh = meshes[index];
        float[]? primitiveData = null;
        foreach (var c in Archetypes(component))
        {
            var overrides = c.GetOrDefault("OverrideMaterials", Array.Empty<UMaterialInterface?>());
            for (var slot = 0; slot < overrides.Length; slot++)
            {
                // (the nearest class's wins; the item's own WeaponMaterialOverrides are already there)
                if (overrides[slot] is not { } material || mesh.OverrideMaterials.Any(m => m.Slot == slot)) continue;
                mesh.OverrideMaterials.AddIfNotNull(Material(material, slot));
            }
            if (primitiveData is null && c.TryGetValue(out FStructFallback custom, "CustomPrimitiveData")
                && custom.GetOrDefault<float[]>("Data") is { Length: > 0 } floats)
                primitiveData = floats;
        }
        if (primitiveData is not null)
            meshes[index] = new MaterialPorterMesh(mesh) { MPPrimitiveData = primitiveData };
    }

    /// <summary>A wrap's values: everything its material's instance chain sets (the child's win).</summary>
    public ParamSet? WrapValues(UObject wrap)
    {
        if (!wrap.GetOrDefault<FSoftObjectPath>("ItemWrapMaterial").TryLoad(out UMaterialInstanceConstant? material))
            return null;
        var values = new ParamSet { Label = wrap.Name };
        for (UUnrealMaterial? current = material; current is UMaterialInstanceConstant instance; current = instance.Parent)
        {
            foreach (var p in instance.ScalarParameterValues) values.Scalars.TryAdd(p.Name, p.ParameterValue);
            foreach (var p in instance.VectorParameterValues)
                if (p.ParameterValue is { } c) values.Vectors.TryAdd(p.Name, [c.R, c.G, c.B, c.A]);
            foreach (var p in instance.TextureParameterValues)
                if (p.ParameterValue.Load<UTexture>() is { } texture) values.Textures.TryAdd(p.Name, texture.GetPathName());
            foreach (var p in instance.StaticParameters?.StaticSwitchParameters ?? [])
                values.Switches.TryAdd(p.Name, p.Value);
        }
        return values;
    }

    /// <summary>
    /// A wrap over each of a mesh's materials: its values, but for the textures the material sets itself
    /// (the weapon's diffuse, normals, masks and customization mask stay its own).
    /// </summary>
    public void ApplyWrap(ExportMesh mesh, ParamSet wrap)
    {
        var slots = mesh.Materials.Select(m => m.Slot).Concat(mesh.OverrideMaterials.Select(m => m.Slot)).Distinct().ToList();
        foreach (var slot in slots)
        {
            var at = mesh.OverrideMaterials.FindIndex(m => m.Slot == slot);
            var material = at >= 0 ? mesh.OverrideMaterials[at] : mesh.Materials.First(m => m.Slot == slot);
            var own = material.Textures.Select(t => t.Name).ToHashSet();
            var values = new ParamSet();
            if (material is MaterialPorterMaterial { MPValues: { } already }) values.MergeFrom(already);
            values.MergeFrom(new ParamSet
            {
                Scalars = wrap.Scalars, Vectors = wrap.Vectors, Switches = wrap.Switches, Label = wrap.Label,
                Textures = wrap.Textures.Where(t => !own.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value),
            });
            var wrapped = new MaterialPorterMaterial(material with { Slot = slot })
            {
                MPValues = values,
                Hash = HashCode.Combine(material.Hash, values.Key()),
            };
            if (at >= 0) mesh.OverrideMaterials[at] = wrapped;
            else mesh.OverrideMaterials.Add(wrapped);
        }
    }
}
