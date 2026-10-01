using System;

namespace FortnitePorting.MaterialPorter;

/// <summary>
/// The fork's own names, so it runs beside an installed FortnitePorting
/// without touching it: its settings and data folders, its single-instance
/// pipe and mutex, its Blender startup module and port, its bridge port.
/// FORTNITEPORTING_MP_PROFILE (e.g. "test") runs a separate instance with
/// its own folders, lock and bridge port - tests never touch the one in use.
/// </summary>
public static class Fork
{
    public static readonly string Profile = Environment.GetEnvironmentVariable("FORTNITEPORTING_MP_PROFILE")?.Trim() ?? "";
    public static string AppFolder => Profile.Length == 0 ? "FortnitePorting MP" : $"FortnitePorting MP {Profile}";
    public static string InstancePipe => "FortnitePortingMP" + Profile;
    public static string InstanceMutex => "FortnitePortingMPMutex" + Profile;
    /// <summary>
    /// Where Blender asks for exact materials (Material Porter's own app uses 24300). FORTNITEPORTING_MP_BRIDGE_PORT
    /// sets it, so two test profiles can run at once.
    /// </summary>
    public static int BridgePort => int.TryParse(Environment.GetEnvironmentVariable("FORTNITEPORTING_MP_BRIDGE_PORT"), out var port) && port > 0
        ? port
        : Profile.Length == 0 ? 24320 : 24322;
    /// <summary>The Blender plugin's folder in Blender's scripts/startup (its Python package name).</summary>
    public const string PluginFolder = "fortnite_porting_mp";
    /// <summary>Upstream's Blender plugin listens on 40000.</summary>
    public const int BlenderPort = 40010;

    /// <summary>
    /// UEFN island export (maps of downloaded islands, unlocking islands by map code): only in the owner's
    /// builds, where the git-ignored FortnitePorting/Fork.local.props sets MPIslands. Releases and other
    /// builds don't have it, as upstream keeps it to the accounts it allows.
    /// </summary>
#if MP_ISLANDS
    public const bool Islands = true;
#else
    public const bool Islands = false;
#endif
}
