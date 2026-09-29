using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FortnitePorting.MaterialPorter;

/// <summary>
/// UEFN island materials an FP import brought in without their textures. FP exports only
/// texture parameters; an island's masters hard-code their textures (only the cooked
/// UMaterial's ReferencedTextures list them) or are plain colours (the cooked parameter
/// defaults, CachedExpressionData). The Blender plugin's Material Fixer asks for them here:
/// a port of the fpisland tool, reading what the app has mounted (the owner's builds only,
/// as islands are).
/// </summary>
public static class IslandMaterials
{
    private static readonly Regex Island = new(@"(^|/)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/", RegexOptions.IgnoreCase);
    private static readonly JsonSerializerSettings Ser = new() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

    public static bool IsIsland(string path) => Island.IsMatch(path);

    /// <summary>"/53577e3c/X/Y.Y" and "FortniteGame/Plugins/GameFeatures/53577e3c/Content/X/Y" -> "53577e3c/x/y".</summary>
    public static string Norm(string p)
    {
        p = p.Split('.')[0].TrimStart('/').ToLowerInvariant();
        p = Regex.Replace(p, @"^fortnitegame/plugins/(gamefeatures/)?([^/]+)/content/", "$2/");
        return Regex.Replace(p, @"^fortnitegame/content/", "game/");
    }

    /// <summary>The packages with these names: name -> file keys (no extension), an island's first. One pass over the file table.</summary>
    public static JObject Find(IFileProvider provider, IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in provider.Files.Keys)
        {
            if (!key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".o.uasset", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(key);
            if (!wanted.Contains(name)) continue;
            if (!found.TryGetValue(name, out var l)) found[name] = l = [];
            var k = key[..^".uasset".Length];
            if (!l.Contains(k)) l.Add(k);
        }
        var result = new JObject();
        foreach (var (name, keys) in found)
            result[name] = new JArray(keys.OrderByDescending(IsIsland).ThenBy(k => k, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>A static mesh's material slots, normalised ("" for a slot with none).</summary>
    public static async Task<JArray> MeshSlotsAsync(IFileProvider provider, string key)
    {
        var mesh = await provider.LoadPackageObjectAsync<UStaticMesh>(key + "." + Path.GetFileName(key));
        return new JArray((mesh.StaticMaterials ?? []).Select(sm => sm.MaterialInterface?.ResolvedObject is { } ro ? Norm(ro.GetPathName()) : ""));
    }

    /// <summary>
    /// A material's record: its chain, each texture it references (an instance's texture
    /// parameters, then its master's ReferencedTextures, as "(referenced)"), its instances'
    /// scalars and vectors (the child's win) and its master's cooked parameter defaults.
    /// </summary>
    public static async Task<JObject> MaterialAsync(IFileProvider provider, string key)
    {
        var rec = new JObject { ["package"] = key, ["norm"] = Norm(key), ["island"] = IsIsland(key) };
        var chain = new JArray();
        var textures = new JArray();
        var vectors = new JObject();
        var scalars = new JObject();
        var cooked = new JObject();
        UUnrealMaterial? cur = await provider.LoadPackageObjectAsync<UUnrealMaterial>(key + "." + Path.GetFileName(key));
        void Texture(UTexture tex, string param, string from) => textures.Add(new JObject
        {
            ["path"] = tex.GetPathName(), ["param"] = param, ["from"] = from,
            ["compression"] = tex.CompressionSettings.ToString(), ["srgb"] = tex.SRGB,
        });
        for (var guard = 0; cur != null && guard < 12; guard++)
        {
            chain.Add($"{cur.ExportType} {cur.GetPathName().Split('.')[0]}");
            if (cur is UMaterialInstanceConstant mic)
            {
                foreach (var tp in mic.TextureParameterValues)
                    if (tp.ParameterValue.Load<UTexture>() is { } tex) Texture(tex, tp.Name, mic.Name);
                foreach (var vp in mic.VectorParameterValues)
                    if (vp.ParameterValue is { } c && !vectors.ContainsKey(vp.Name)) vectors[vp.Name] = new JArray(c.R, c.G, c.B, c.A);
                foreach (var sp in mic.ScalarParameterValues)
                    if (!scalars.ContainsKey(sp.Name)) scalars[sp.Name] = sp.ParameterValue;
            }
            if (cur is UMaterialInstance mi)
            {
                cur = mi.Parent;
                continue;
            }
            if (cur is UMaterial um)
            {
                foreach (var tex in um.ReferencedTextures)
                    if (tex is not null) Texture(tex, "(referenced)", um.Name);
                // the master's parameter defaults: each runtime parameter type's names beside its values
                if (um.CachedExpressionData is { } ced && JToken.Parse(JsonConvert.SerializeObject(ced, Ser)) is JObject ce)
                {
                    JArray Names(int type) => ce[type == 0 ? "RuntimeEntries" : $"RuntimeEntries[{type}]"]?["ParameterInfoSet"] as JArray ?? [];
                    var sv = ce["ScalarValues"] as JArray ?? [];
                    var vv = ce["VectorValues"] as JArray ?? [];
                    var n = Names(0);
                    for (var i = 0; i < n.Count && i < sv.Count; i++)
                        if ((string?)n[i]["Name"] is { } name) cooked[name] = sv[i];
                    n = Names(1);
                    for (var i = 0; i < n.Count && i < vv.Count; i++)
                        if ((string?)n[i]["Name"] is { } name && vv[i] is JObject v)
                            cooked[name] = new JArray((double?)v["R"] ?? 0, (double?)v["G"] ?? 0, (double?)v["B"] ?? 0, (double?)v["A"] ?? 1);
                }
            }
            break;
        }
        rec["chain"] = chain;
        rec["textures"] = textures;
        rec["vectors"] = vectors;
        rec["scalars"] = scalars;
        rec["cooked"] = cooked;
        return rec;
    }
}
