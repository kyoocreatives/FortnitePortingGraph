using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Actor;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using FortnitePorting.Exporting.MaterialPorter;
using FortnitePorting.Exporting.Models;
using FortnitePorting.Shared.Extensions;
using Serilog;

namespace FortnitePorting.Exporting.Context;

/// <summary>
/// Material Porter fork: a level read by Material Porter's map reader. Each
/// actor's components come from the level's own exports over their class's
/// templates: attachments, overrides, dynamic material instances, texture
/// data (textures and tints), custom primitive and per-instance data,
/// level instances, spline meshes (bent in Blender), water bodies; HLODs,
/// devices, hidden actors, ziplines and meshes drawn only into the terrain's
/// virtual texture are left out. Landscapes and FP's HLOD export still go
/// through FP's own actor export.
/// </summary>
public partial class ExportContext
{
    public static bool UseMaterialPorterMaps = true;

    private readonly Dictionary<string, ExportMesh?> _mpMeshes = new(StringComparer.OrdinalIgnoreCase);

    public List<ExportMesh>? MaterialPorterLevel(ULevel level)
    {
        if (!UseMaterialPorterMaps || level.Owner?.Name is not { } package) return null;

        var meshes = new List<ExportMesh>();
        var actors = Meta.WorldFlags.HasFlag(EWorldFlags.Actors);
        if (actors)
        {
            var options = new MapOptions
            {
                Instances = Meta.WorldFlags.HasFlag(EWorldFlags.InstancedFoliage),
                Landscape = false,
            };
            var scan = new MapScan { Name = package, Key = package };
            var reader = new MapReader(new MapGame { Provider = FileProvider }, options, scan);
            try
            {
                reader.LevelAsync(package, Matrix4x4.Identity, 0, CancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return meshes;
            }

            var placed = reader.Placed.OrderBy(m => m.Actor, StringComparer.Ordinal).ThenBy(m => m.Mesh, StringComparer.Ordinal).ToList();
            var done = 0;
            foreach (var m in placed)
            {
                if (CancellationToken.IsCancellationRequested) break;
                if (++done % 200 == 0) Meta.OnUpdateProgress(m.Actor, done, placed.Count);
                meshes.AddIfNotNull(Placement(m));
            }
            Log.Information("[Material Porter] {Level}: {Count} placements, skipped {Skipped}", package, meshes.Count,
                string.Join(", ", scan.Skipped.Select(kv => $"{kv.Value} {kv.Key}")));
        }

        // the terrain and FP's HLODs, as FP exports them
        foreach (var actorLazy in level.Actors)
        {
            if (CancellationToken.IsCancellationRequested) break;
            if (actorLazy is null || actorLazy.IsNull) continue;
            var type = actorLazy.ResolvedObject?.Class?.Name.Text ?? string.Empty;
            if (!type.Contains("Landscape", StringComparison.Ordinal) && type != "FortMainHLOD") continue;
            if (actorLazy.Load() is not { } actor) continue;
            if (actor is ALandscapeProxy or { ExportType: "FortMainHLOD" })
                meshes.AddRange(Actor(actor, loadTemplate: false).Where(x => x is not null));
        }

        return meshes;
    }

    /// <summary>One placement as FP's mesh record: the mesh (exported once), where it stands, what its slots wear.</summary>
    private ExportMesh? Placement(MapMesh m)
    {
        if (!_mpMeshes.TryGetValue(m.Mesh, out var template))
        {
            template = LoadMaterialPorterObject(m.Mesh) switch
            {
                UStaticMesh staticMesh => Mesh(staticMesh),
                USkeletalMesh skeletalMesh => Mesh(skeletalMesh),
                _ => null
            };
            _mpMeshes[m.Mesh] = template;
        }
        if (template is null) return null;

        var export = new MaterialPorterMesh
        {
            Name = template.Name,
            Path = template.Path,
            NumLods = template.NumLods,
            MPPrimitiveData = m.PrimitiveData,
            MPInstanceData = m.InstanceData,
            MPSpline = m.Spline,
        };
        export.Materials.AddRange(template.Materials);
        SetMaterialPorterTransform(export, m.World);

        // each slot's material: a component's override, else the mesh's own; a dynamic instance's
        // values and a building's texture data over it
        var slots = template.Materials.Select(x => x.Slot).Concat(m.Overrides.Keys).Distinct();
        foreach (var slot in slots)
        {
            ExportMaterial? material = null;
            if (m.Overrides.TryGetValue(slot, out var path))
            {
                if (LoadMaterialPorterObject(path) is UMaterialInterface mi) material = Material(mi, slot);
            }
            var values = new ParamSet();
            if (m.Params.TryGetValue(slot, out var mine)) values.MergeFrom(mine);
            if (m.AllSlots is { } skin) values.MergeFrom(skin);
            var hasValues = values.Scalars.Count + values.Vectors.Count + values.Textures.Count > 0;
            if (material is null && !hasValues) continue;

            material ??= template.Materials.FirstOrDefault(x => x.Slot == slot);
            if (material is null) continue;
            if (hasValues)
            {
                material = new MaterialPorterMaterial(material with { Slot = slot })
                {
                    MPValues = values,
                    Hash = HashCode.Combine(material.Hash, values.Key()),
                };
            }
            else material = material with { Slot = slot };
            export.OverrideMaterials.Add(material);
        }
        return export;
    }

    /// <summary>A UE world matrix (row vectors) as FP's location, rotation and scale; a mirror goes into X's scale.</summary>
    private static void SetMaterialPorterTransform(ExportObject export, Matrix4x4 world)
    {
        var mirrored = world.GetDeterminant() < 0;
        if (mirrored) world = Matrix4x4.CreateScale(-1, 1, 1) * world;
        if (!Matrix4x4.Decompose(world, out var scale, out var rotation, out var translation))
        {
            scale = Vector3.One;
            rotation = Quaternion.Identity;
            translation = world.Translation;
        }
        if (mirrored) scale.X = -scale.X;
        export.Location = new FVector(translation.X, translation.Y, translation.Z);
        export.Rotation = new FQuat(rotation.X, rotation.Y, rotation.Z, rotation.W).Rotator();
        export.Scale = new FVector(scale.X, scale.Y, scale.Z);
    }

    /// <summary>An object by its path, also one nested in a level ("/Map.Map:PersistentLevel.Actor.Component.WaterInfoMesh_0").</summary>
    private UObject? LoadMaterialPorterObject(string path)
    {
        try
        {
            if (!path.Contains(':')) return FileProvider.TryLoadPackageObject(path, out var obj) ? obj : null;
            var slash = path.LastIndexOf('/');
            var package = FileProvider.LoadPackage(path[..path.IndexOf('.', slash)]);
            return package.GetExports().FirstOrDefault(e => e.GetPathName() == path);
        }
        catch (Exception e)
        {
            Log.Warning("[Material Porter] {Path}: {Message}", path, e.Message);
            return null;
        }
    }
}
