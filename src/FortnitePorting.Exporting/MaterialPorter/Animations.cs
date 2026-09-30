using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>
/// Material Porter fork: the Animations tab. The cooked asset registry lists few of the game's
/// animations (2,248 sequences, mostly islands'), so they are found by path (an animation folder, an
/// anim or montage in the name: 210,000 candidates of 2 million packages), each checked and outlined
/// from its package's export and import maps without being read: its class, its skeleton, what it
/// is for (<see cref="Kinds"/>, the tab's filters).
/// </summary>
public static partial class Animations
{
    public static readonly string[] Classes = ["AnimSequence", "AnimMontage"];

    /// <summary>What an animation is for, from its path and skeleton, the first that matches: the tab's filters.</summary>
    public static readonly (string Kind, Regex Match)[] Kinds =
    [
        ("Gliders", new Regex("glider", RegexOptions.IgnoreCase)),
        ("Back Blings", new Regex("backpack|backbling|/capes?/|_cape", RegexOptions.IgnoreCase)),
        ("Pickaxes", new Regex("pickaxe|/melee/", RegexOptions.IgnoreCase)),
        ("Emotes", new Regex("/emotes?/|/dances?/|emote_", RegexOptions.IgnoreCase)),
        ("Creatures", new Regex("junocreature|wildlife|creature|/animals?/|/ai/|/npcs?/", RegexOptions.IgnoreCase)),
        ("Pets", new Regex("companion|/pets?/|petcarrier", RegexOptions.IgnoreCase)),
        ("Vehicles", new Regex("vehicle|/cars?/|valet", RegexOptions.IgnoreCase)),
        ("Weapons", new Regex("/weapons?/|weapon", RegexOptions.IgnoreCase)),
        ("LEGO", new Regex("^/juno|figurecosmetics|lego|minifig|sk_figure", RegexOptions.IgnoreCase)),
        ("Characters", new Regex("mainplayer|/characters?/|/player/|player_skeleton|base_skeleton|female|male", RegexOptions.IgnoreCase)),
    ];

    [GeneratedRegex(@"/anim|anim_|_anim|montage|/emotes?/|/dances?/", RegexOptions.IgnoreCase)]
    private static partial Regex Candidate();

    public sealed record Outline(string? Class, string? Skeleton, string Kind) : Unloaded.IOutline
    {
        public string Describe(string folder) =>
            $"{(Class == "AnimMontage" ? "Montage" : "Sequence")}{(Skeleton is null ? "" : $" · {Skeleton}")} · {folder}";
    }

    /// <summary>The packages that may be animations, by path: (package path as the game mounts it, object name).</summary>
    public static List<(string Package, string Name)> Candidates(IFileProvider provider, IEnumerable<(string Package, string Name)> registry)
    {
        var found = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (package, name) in registry) found.TryAdd(package, (package, name));
        foreach (var path in provider.Files.Keys)
        {
            if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".o.uasset", StringComparison.OrdinalIgnoreCase)
                || !Candidate().IsMatch(path)) continue;
            var package = Effects.MountPath(path[..^".uasset".Length]);
            found.TryAdd(package, (package, package[(package.LastIndexOf('/') + 1)..]));
        }
        return found.Values.ToList();
    }

    // the outlines of an earlier listing of the same files (reading 130,000 packages' maps takes over a minute)
    private static ConcurrentDictionary<string, Outline> _known = new(StringComparer.OrdinalIgnoreCase);
    private static string? _knownFile, _knownKey;

    /// <summary>The outlines kept in the file from a listing of the same files (key: what says they are the same).</summary>
    public static void Recall(string file, string key)
    {
        _knownFile = file;
        _knownKey = key;
        _known = new ConcurrentDictionary<string, Outline>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(file)) return;
            using var reader = new StreamReader(file);
            if (reader.ReadLine() != key) return;
            while (reader.ReadLine() is { } line)
            {
                var f = line.Split('\t');
                if (f.Length == 4) _known[f[0]] = new Outline(f[1].Length > 0 ? f[1] : null, f[2].Length > 0 ? f[2] : null, KindOf(f[0], f[2]));
            }
        }
        catch (Exception)
        {
            _known.Clear();
        }
    }

    /// <summary>Keeps this listing's outlines for the next.</summary>
    public static void Remember()
    {
        if (_knownFile is null || _knownKey is null) return;
        try
        {
            using var writer = new StreamWriter(_knownFile);
            writer.WriteLine(_knownKey);
            foreach (var (package, o) in _known)
                writer.WriteLine($"{package}\t{o.Class}\t{o.Skeleton}\t{o.Kind}");
        }
        catch (Exception)
        {
            // (only the next listing is slower)
        }
    }

    /// <summary>An animation's outline from its package's maps: its class, its skeleton (the import of that class), what it is for.</summary>
    public static async Task<Outline?> ReadOutline(IFileProvider provider, string package, string name)
    {
        if (_known.TryGetValue(package, out var known)) return known;
        var outline = await ReadMaps(provider, package, name);
        if (outline is not null) _known[package] = outline;
        return outline;
    }

    private static async Task<Outline?> ReadMaps(IFileProvider provider, string package, string name)
    {
        try
        {
            var loaded = await provider.LoadPackageAsync(package);
            var index = loaded.GetExportIndex(name, StringComparison.OrdinalIgnoreCase);
            var type = index < 0 ? null : loaded.ResolvePackageIndex(new FPackageIndex(loaded, index + 1))?.Class?.Name.Text;
            if (type is null || !Classes.Contains(type)) return new Outline(type, null, "");
            string? skeleton = null;
            for (var i = 0; i < loaded.ImportMapLength && skeleton is null; i++)
                if (loaded.ResolvePackageIndex(new FPackageIndex(loaded, -(i + 1))) is { } import && import.Class?.Name.Text == "Skeleton")
                    skeleton = import.Name.Text;
            return new Outline(type, skeleton, KindOf(package, skeleton));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>What an animation is for, from its path and its skeleton's name.</summary>
    public static string KindOf(string package, string? skeleton)
    {
        var about = $"{package} {skeleton}";
        return Kinds.FirstOrDefault(k => k.Match.IsMatch(about)).Kind ?? "Other";
    }

    /// <summary>What the tab says of an animation listed unread.</summary>
    public static string Describe(UObject animation)
    {
        var path = animation.GetPathName();
        var folder = path[..Math.Max(0, path.LastIndexOf('/'))];
        return Unloaded.Detail(animation) is Outline outline ? outline.Describe(folder) : folder;
    }

    public static bool Is(UObject animation, string kind) => (Unloaded.Detail(animation) as Outline)?.Kind == kind;
}
