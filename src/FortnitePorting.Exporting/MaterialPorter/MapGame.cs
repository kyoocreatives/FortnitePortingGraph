using System.Collections.Generic;
using CUE4Parse.FileProvider;
using FortnitePorting.Exporting.Models;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>What Material Porter's map reader reads of the game.</summary>
public sealed class MapGame
{
    public IFileProvider Provider { get; init; } = null!;
}

/// <summary>A placement's extras for the fork's Blender plugin (FP's record plus these fields).</summary>
public record MaterialPorterMesh : ExportMesh
{
    /// <summary>UE's Custom Primitive Data (texture data tints at 30/31/35), or null.</summary>
    public float[]? MPPrimitiveData;
    /// <summary>An instance's own custom data floats (the materials' PerInstanceCustomData), or null.</summary>
    public float[]? MPInstanceData;
    /// <summary>A spline mesh's bend, bent in Blender (Material Porter's meshes.spline_bend), or null.</summary>
    public Dictionary<string, object>? MPSpline;
    /// <summary>A landscape's weight layers: LayerInfo asset name (FP's colour layer name) -> LayerName.</summary>
    public Dictionary<string, string>? MPLayerNames;
}

/// <summary>A material with values over it: a dynamic instance's, a building's texture data.</summary>
public record MaterialPorterMaterial : ExportMaterial
{
    public MaterialPorterMaterial(ExportMaterial material) : base(material) { }
    public ParamSet? MPValues;
    /// <summary>A LEGO figure's face: where its rig puts the character accents for each mouth pose (FigureRecipe.AccentRigAsync).</summary>
    public string? MPFaceRig;
}
