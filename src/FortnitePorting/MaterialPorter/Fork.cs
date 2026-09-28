namespace FortnitePorting.MaterialPorter;

/// <summary>
/// The fork's own names, so it runs beside an installed FortnitePorting
/// without touching it: its settings and data folders, its single-instance
/// pipe and mutex, its Blender startup module and port.
/// </summary>
public static class Fork
{
    public const string AppFolder = "FortnitePorting MP";
    public const string InstancePipe = "FortnitePortingMP";
    public const string InstanceMutex = "FortnitePortingMPMutex";
    /// <summary>The Blender plugin's folder in Blender's scripts/startup (its Python package name).</summary>
    public const string PluginFolder = "fortnite_porting_mp";
    /// <summary>Upstream's Blender plugin listens on 40000.</summary>
    public const int BlenderPort = 40010;
}
