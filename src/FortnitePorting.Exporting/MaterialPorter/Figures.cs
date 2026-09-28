using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using FortnitePorting.CUE4Parse.Extensions;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>
/// LEGO figures (JunoAthenaCharacterItemOverrideDefinition). A figure's AssembledMeshSchema is
/// either baked, listing its cooked skeletal meshes (SkeletalMeshes), or a Mutable object
/// (CustomizableObjectInstance). A Mutable figure may still have a bake cooked in its Bake folder,
/// beside the Mutable one (/FigureCosmetics/Figure/Figure_X/Mutable/... and .../Figure_X/Bake/).
/// </summary>
public static class Figures
{
    public const string ItemClass = "JunoAthenaCharacterItemOverrideDefinition";

    private static readonly object Lock = new();
    private static IFileProvider? _indexed;
    private static Dictionary<string, List<string>> _bakes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The figure's AssembledMeshSchema (a soft reference on the item), loaded.</summary>
    public static UObject? Schema(UObject item, IFileProvider? provider = null)
    {
        foreach (var name in (string[]) ["AssembledMeshSchema", "LowDetailsAssembledMeshSchema"])
        {
            if (!item.TryGetValue(out FSoftObjectPath path, name) || path.AssetPathName.IsNone || path.AssetPathName.Text.Length == 0) continue;
            if (provider is null ? path.TryLoad(out UObject? schema) : path.TryLoad(provider, out schema)) return schema;
        }
        return null;
    }

    /// <summary>The references a baked schema lists (SkeletalMeshes: soft or hard, or in structs), unloaded.</summary>
    private static IEnumerable<object> SchemaMeshRefs(UObject schema)
    {
        if (schema.Properties.FirstOrDefault(p => p.Name.Text == "SkeletalMeshes")?.Tag?.GenericValue is not UScriptArray meshes)
            yield break;
        foreach (var value in meshes.Properties.Select(p => p.GenericValue))
        {
            if (value is FScriptStruct { StructType: FStructFallback fields })
            {
                foreach (var field in fields.Properties.Select(p => p.Tag?.GenericValue))
                    if (field is FSoftObjectPath or FPackageIndex) yield return field;
            }
            else if (value is FSoftObjectPath or FPackageIndex)
                yield return value;
        }
    }

    private static USkeletalMesh? LoadMesh(IFileProvider provider, object reference)
    {
        try
        {
            return reference switch
            {
                FSoftObjectPath soft when !soft.AssetPathName.IsNone => soft.TryLoad(provider, out UObject? o) ? o as USkeletalMesh : null,
                FPackageIndex index when !index.IsNull => index.Load() as USkeletalMesh,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The figure's Bake folder (a file key, no trailing slash) beside its Mutable object, or null.</summary>
    public static string? BakeFolder(IFileProvider provider, UObject? schema)
    {
        if (schema is null || !schema.TryGetValue(out FSoftObjectPath instance, "CustomizableObjectInstance")) return null;
        var path = instance.AssetPathName.Text;
        if (string.IsNullOrEmpty(path) || path == "None") return null;
        var dot = path.LastIndexOf('.');
        if (dot > path.LastIndexOf('/')) path = path[..dot];
        string key;
        try { key = provider.FixPath(path); }
        catch { return null; }
        var mutable = key.IndexOf("/Mutable/", StringComparison.OrdinalIgnoreCase);
        return mutable < 0 ? null : key[..mutable] + "/Bake";
    }

    /// <summary>The packages in a figure's Bake folder that may be its mesh (not its materials or textures).</summary>
    private static IReadOnlyList<string> BakeCandidates(IFileProvider provider, UObject? schema)
    {
        if (BakeFolder(provider, schema) is not { } folder) return [];
        return Index(provider).TryGetValue(folder, out var found) ? found : [];
    }

    /// <summary>Whether the figure has cooked meshes (listed by its schema, or in its Bake folder), without loading them.</summary>
    public static bool HasBake(IFileProvider provider, UObject item)
    {
        var schema = Schema(item, provider);
        return schema is not null && (SchemaMeshRefs(schema).Any() || BakeCandidates(provider, schema).Count > 0);
    }

    /// <summary>The figure's cooked skeletal meshes: its schema's, or else its Bake folder's.</summary>
    public static List<USkeletalMesh> BakedMeshes(IFileProvider provider, UObject item)
    {
        var schema = Schema(item, provider);
        if (schema is null) return [];
        var meshes = SchemaMeshRefs(schema).Select(r => LoadMesh(provider, r)).OfType<USkeletalMesh>()
            .DistinctBy(m => m.GetPathName()).ToList();
        if (meshes.Count > 0) return meshes;
        foreach (var key in BakeCandidates(provider, schema))
        {
            try
            {
                if (provider.LoadPackage(key).GetExports().OfType<USkeletalMesh>().FirstOrDefault() is { } mesh) return [mesh];
            }
            catch
            {
                // not a package that loads: the next one
            }
        }
        return [];
    }

    /// <summary>Why a figure has no cooked mesh: its schema's properties and Bake folder, for the log.</summary>
    public static string Trace(IFileProvider provider, UObject item)
    {
        var schema = Schema(item, provider);
        if (schema is null) return "no schema";
        var props = string.Join(" ", schema.Properties.Select(p => $"{p.Name.Text}:{p.Tag?.GenericValue?.GetType().Name}"));
        var refs = string.Join(" ", SchemaMeshRefs(schema).Select(r => r.ToString()));
        return $"schema {schema.GetPathName()} ({props}), meshes [{refs}], bake folder {BakeFolder(provider, schema) ?? "none"}";
    }

    /// <summary>Bake folder -> its packages named like a mesh (FigureBake_X, SK_FigureBake_X_...), made once per provider.</summary>
    private static Dictionary<string, List<string>> Index(IFileProvider provider)
    {
        lock (Lock)
        {
            if (ReferenceEquals(_indexed, provider)) return _bakes;
            var bakes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in provider.Files.Keys)
            {
                if (!key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
                var slash = key.LastIndexOf('/');
                if (slash < 5 || !key.AsSpan(0, slash).EndsWith("/Bake", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(key);
                // the bake's materials and textures are named after the mesh: FigureBake_X_M_Figure_...
                if (!name.Contains("FigureBake_", StringComparison.OrdinalIgnoreCase) || name.Contains("_M_", StringComparison.OrdinalIgnoreCase)) continue;
                var folder = key[..slash];
                if (!bakes.TryGetValue(folder, out var list)) bakes[folder] = list = [];
                list.Add(key[..^".uasset".Length]);
            }
            foreach (var list in bakes.Values)
                list.Sort((a, b) => a.Length.CompareTo(b.Length));     // FigureBake_X before its variants
            _bakes = bakes;
            _indexed = provider;
            return bakes;
        }
    }
}
