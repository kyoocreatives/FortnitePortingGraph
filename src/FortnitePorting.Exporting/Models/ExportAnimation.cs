using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.Core.Math;
using Newtonsoft.Json;

namespace FortnitePorting.Exporting.Models;

public class ExportAnimSection
{
    public string Path;
    public string Name;
    public float Time;
    public float Length;
    public float LinkValue;
    public bool Loop;
    /// <summary>
    /// Material Porter fork, a LEGO emote's section: its float curves' key interpolation, one
    /// letter per key in the .ueanim's order ("C" constant, "L" linear, "Q" cubic), one letter
    /// for a curve whose keys all share it. The face rig's pose curves step.
    /// </summary>
    public Dictionary<string, string>? MPCurveModes;

    [JsonIgnore] public UAnimSequence AssetRef;
}

public class ExportProp
{
    public ExportMesh Mesh;
    public List<ExportAnimSection> AnimSections;
    public string SocketName;
    public FVector LocationOffset;
    public FRotator RotationOffset;
    public FVector Scale;
}