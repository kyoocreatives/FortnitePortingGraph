using System;
using System.Diagnostics;
using System.IO;
using CUE4Parse.FileProvider;
using FortnitePorting.Application;
using FortnitePorting.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FortnitePorting.MaterialPorter;

/// <summary>
/// What Material Porter's ported code (MaterialService, Bridge) reads of the
/// game: FP's provider, the build it mounted, where its cache lives.
/// </summary>
public class GameContext
{
    /// <summary>Material Porter's data folder: its cache (graphs, textures) is shared with the Material Porter app.</summary>
    public static string DataDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialPorter");

    public IFileProvider Provider { get; set; } = null!;
    public string BuildVersion { get; set; } = "unknown";
    public bool Mounted => Provider != null;
}

/// <summary>Material Porter's timing lines, into FP's log at debug level.</summary>
public static class Timing
{
    public static void Log(string what, Stopwatch sw) =>
        Serilog.Log.Debug("[Material Porter] {What}: {Ms:0} ms", what, sw.Elapsed.TotalMilliseconds);
}

/// <summary>
/// Exact materials: while FP's Blender plugin imports, it asks this app on
/// localhost (Material Porter's bridge) for each material's description, its
/// graph and its functions' graphs, textures and parameter collections, and
/// rebuilds the material from its UE graph instead of FP's presets.
/// </summary>
public class MaterialPorterService : IService
{
    public const int Port = 24320;

    public static MaterialPorterService Instance => AppServices.Services.GetRequiredService<MaterialPorterService>();

    public GameContext Game { get; } = new();
    public MaterialService? Materials { get; private set; }
    private Bridge? _bridge;

    /// <summary>Once the game's files are mounted: the bridge starts listening (again after a reload).</summary>
    public void OnGameLoaded(IFileProvider provider, string? buildVersion)
    {
        Game.Provider = provider;
        Game.BuildVersion = string.IsNullOrWhiteSpace(buildVersion) ? "unknown" : buildVersion;
        Materials = new MaterialService(Game);
        _bridge?.Dispose();
        try
        {
            _bridge = new Bridge(Game, Materials);
            _bridge.Log += message => Log.Information("[Material Porter] {Message}", message);
            _bridge.Start(Port);
            Log.Information("[Material Porter] exact materials served on localhost:{Port} ({Build})", Port, Game.BuildVersion);
        }
        catch (Exception e)
        {
            _bridge = null;
            Log.Error(e, "[Material Porter] could not listen on localhost:{Port}; Blender falls back to FP's materials", Port);
        }
    }
}
