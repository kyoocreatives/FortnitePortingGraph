using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>
/// Material Porter fork: an asset listed without being read, for a tab of tens of thousands (the
/// game's Niagara systems: holding them all read took over 40 GB). It has the asset's name, path and
/// class, and what the listing learnt of it without reading it (<see cref="Detail"/>); the export
/// reads the asset itself (<see cref="Read"/>).
/// </summary>
public static class Unloaded
{
    private sealed record Marker(string Path, object? Detail);

    public static UObject Create(string package, string name, string className, object? detail = null) => new()
    {
        Name = name,
        Outer = new ResolvedLoadedObject(new UObject { Name = package }),
        Class = new ResolvedLoadedObject(new UObject { Name = className }),
        CustomGameData = new Marker($"{package}.{name}", detail),
    };

    public static bool Is(UObject asset) => asset.CustomGameData is Marker;

    public static object? Detail(UObject asset) => (asset.CustomGameData as Marker)?.Detail;

    /// <summary>The asset itself where this stands for it.</summary>
    public static UObject Read(UObject asset, IFileProvider provider) =>
        asset.CustomGameData is Marker marker ? provider.LoadPackageObject(marker.Path) : asset;
}
