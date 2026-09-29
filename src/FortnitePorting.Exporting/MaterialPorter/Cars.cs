using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FortnitePorting.Exporting.MaterialPorter;

/// <summary>A registry item a car can take (a decal, a wheel set): its asset path and title.</summary>
public sealed record CarItem(string Name, string Title, string Package, string ObjectPath);

public sealed class CarOption
{
    public string Name { get; init; } = "";
    /// <summary>A preview texture's object path, or null.</summary>
    public string? Icon { get; init; }
    /// <summary>An sRGB swatch ("RRGGBB") for a colour option, or null.</summary>
    public string? Swatch { get; init; }
    /// <summary>The item whose icon stands for this option (a wheel set), or null.</summary>
    public CarItem? IconItem { get; init; }
}

public sealed class CarChannel
{
    public string Name { get; init; } = "";
    public int Default { get; set; }
    public List<CarOption> Options { get; } = [];
}

/// <summary>What a car is under a set of picks: its style channels, and the parts and values those picks give.</summary>
public sealed class CarPlan
{
    public List<CarChannel> Channels { get; } = [];
    /// <summary>The picks made, as "Channel: Option" (the default ones left out).</summary>
    public List<string> Styles { get; } = [];
    public string? BodyMesh { get; set; }
    /// <summary>Body material index -> a decal's material.</summary>
    public Dictionary<int, string> BodyOverrides { get; } = [];
    public string? WheelMesh { get; set; }
    /// <summary>Each wheel's place in the body's space (UE, row vectors) and its label.</summary>
    public List<(string Label, Matrix4x4 Transform)> Wheels { get; } = [];
    /// <summary>A material (by its asset name) -> the values Mutable gives it on this car.</summary>
    public Dictionary<string, ParamSet> Params { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Rocket Racing cars (FortVehicleCosmeticsItemDefinition_Body), as Material
/// Porter assembles them: the tier (VCID_Body*) names the skeletal mesh,
/// default material and wheels; a decal (CarSkin_*) swaps the body material
/// for its own; wheels sit on the body's wheel sockets through the wheel's
/// per-corner offset, turn and mirror; the colours come from the car's
/// Mutable program (CO_VehicleCosmeticsRoot) run with the car's values.
/// A port of Material Porter's Vehicles.cs to the fork (its catalog and
/// style types replaced by CarItem/CarChannel/CarPlan).
/// </summary>
public sealed class Cars(IFileProvider provider)
{
    public const string BodyClass = "FortVehicleCosmeticsItemDefinition_Body";
    public const string SkinClass = "FortVehicleCosmeticsItemDefinition_Skin";
    public const string WheelClass = "FortVehicleCosmeticsItemDefinition_Wheel";

    /// <summary>The registry's decal and wheel items (the app sets it once the game is mounted).</summary>
    public static Func<Task<(List<CarItem> Skins, List<CarItem> Wheels)>>? Items;

    private static readonly JsonSerializerSettings Ser = new() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
    private readonly Dictionary<string, Task<MutableProgram>> _programs = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Tier(string Name, string? Icon, string Vcid, List<string> Tags);
    private sealed record Decal(string Name, string? Icon, string Tier, string Material, JObject? Color, string? Vcid);
    private sealed record Painted(string Name, string? Icon, string Row);

    // ------------------------------------------------------------ JSON helpers (Material Porter's)
    private static string? RefPath(JToken? r)
    {
        if (r is not JObject o) return null;
        var soft = (string?)o["AssetPathName"];
        if (!string.IsNullOrEmpty(soft) && soft != "None") return soft;
        return MaterialService.ObjectPath(o);
    }

    private static string? Tail(string? path) => path?[(path.LastIndexOf('/') + 1)..];
    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(Tail(a), Tail(b), StringComparison.OrdinalIgnoreCase);
    private static string? Text(JToken? t) => (string?)t?["LocalizedString"] ?? (string?)t?["SourceString"];

    private async Task<JObject> PropsAsync(string path)
    {
        var obj = await provider.LoadPackageObjectAsync(path);
        return JObject.Parse(JsonConvert.SerializeObject(obj, Ser))["Properties"] as JObject ?? new JObject();
    }

    private async Task<(JObject Main, Dictionary<string, JObject> Exports)> PackageAsync(string package, string name)
    {
        var pkg = await provider.LoadPackageAsync(package);
        var all = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        JObject? main = null;
        foreach (var e in pkg.GetExports())
        {
            var props = JObject.Parse(JsonConvert.SerializeObject(e, Ser))["Properties"] as JObject ?? new JObject();
            all[e.Name] = props;
            if (e.Name == name) main = props;
        }
        return (main ?? new JObject(), all);
    }

    private static JObject? ExportOf(JToken? r, IReadOnlyDictionary<string, JObject> exports)
    {
        var name = ((string?)r?["ObjectName"] ?? "").Split('\'').ElementAtOrDefault(1)?.Split(':').Last().Split('.').Last();
        return name != null && exports.TryGetValue(name, out var v) ? v : null;
    }

    private static double[]? Rgba(JToken? c)
    {
        if (c is not JObject o) return null;
        double F(string k) => (double?)o[k] ?? 0.0;
        return [F("R"), F("G"), F("B"), o["A"] == null ? 1.0 : F("A")];
    }

    private static string Hex(double[] rgba)
    {
        // linear -> sRGB, for the swatch shown
        static int S(double v) => (int)Math.Round(255 * Math.Clamp(v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055, 0, 1));
        return $"{S(rgba[0]):X2}{S(rgba[1]):X2}{S(rgba[2]):X2}";
    }

    /// <summary>A colour picker for a Mutable colour: "Default" (its start) then the baked swatches.</summary>
    private static CarChannel ColorChannel(string name, JObject? variant)
    {
        var ch = new CarChannel { Name = name };
        if (Rgba(variant?["InlineVariant"]?["RichColorVar"]?["DefaultStartingColor"]) is { } start)
            ch.Options.Add(new CarOption { Name = "Default", Swatch = Hex(start) });
        foreach (var c in variant?["BakedSwatchColors"] as JArray ?? [])
            if (Rgba(c) is { } rgba)
                ch.Options.Add(new CarOption { Name = "#" + ((string?)c["Hex"] ?? Hex(rgba)), Swatch = (string?)c["Hex"] ?? Hex(rgba) });
        return ch;
    }

    private static double[]? SwatchColor(JObject? variant, int pick)
    {
        if (pick <= 0) return Rgba(variant?["InlineVariant"]?["RichColorVar"]?["DefaultStartingColor"]);
        var swatches = variant?["BakedSwatchColors"] as JArray;
        return swatches != null && pick - 1 < swatches.Count ? Rgba(swatches[pick - 1]) : null;
    }

    private static List<Painted> PaintedOf(JObject? variant, out int dflt)
    {
        dflt = 0;
        var list = new List<Painted>();
        foreach (var o in variant?["GenericPropertyOptions"] ?? new JArray())
        {
            var row = (o["CosmeticProperties"] ?? new JArray()).Select(cp => (string?)cp["TableRow"]?["RowName"]).FirstOrDefault(r => r != null);
            if (row == null) continue;
            if ((bool?)o["bIsDefault"] == true) dflt = list.Count;
            list.Add(new Painted(Text(o["VariantName"]) ?? row, RefPath(o["PreviewImage"]), row));
        }
        return list;
    }

    private Task<MutableProgram> ProgramAsync(string co)
    {
        var pkg = co.Split('.')[0];
        lock (_programs)
        {
            if (!_programs.TryGetValue(pkg, out var t)) _programs[pkg] = t = MutableProgram.LoadAsync(provider, pkg);
            return t;
        }
    }

    private async Task<List<(string Slot, string? Material)>> SlotsAsync(string meshPath)
    {
        var obj = await provider.LoadPackageObjectAsync(meshPath);
        return obj switch
        {
            USkeletalMesh sk => sk.SkeletalMaterials.Select(m => (m.MaterialSlotName.Text, m.Material?.ResolvedObject?.GetPathName())).ToList(),
            UStaticMesh sm => sm.StaticMaterials.Select(m => (m.MaterialSlotName.Text, m.MaterialInterface?.ResolvedObject?.GetPathName())).ToList(),
            _ => [],
        };
    }

    /// <summary>An item definition's Mutable settings into the program's values (a name tried with the component's prefix).</summary>
    private static void Put(MutableProgram mp, Dictionary<string, object> vals, JObject def, string prefix)
    {
        string? N(string? n) => n == null ? null : mp.Has(n) ? n : mp.Has(prefix + n) ? prefix + n : null;
        foreach (var g in new[] { def["BodyGroup"], def["WheelGroup"] })
            if (N((string?)g?["ParameterName"]) is { } gn && mp.EnumValue(gn, (string?)g!["ParameterValue"]) is { } gv) vals[gn] = gv;
        foreach (var a in def["AdditionalParametersInfos"] ?? new JArray())
        {
            foreach (var i in a["IntParameters"] ?? new JArray())
                if (N((string?)i["ParameterName"]) is { } n)
                {
                    var v = (string?)i["ParameterValue"];
                    if (mp.EnumValue(n, v) is { } e) vals[n] = e;
                    else if (int.TryParse(v, out var x)) vals[n] = x;
                }
            foreach (var c in a["ColorParameters"] ?? new JArray())
                if (N((string?)c["ParameterName"]) is { } n && Rgba(c["ParameterValue"]) is { } rgba) vals[n] = rgba;
            foreach (var f in a["FloatParameters"] ?? new JArray())
                if (N((string?)f["ParameterName"]) is { } n && (double?)f["ParameterValue"] is { } x) vals[n] = x;
            foreach (var m in a["MaterialParameters"] ?? new JArray())
                if (N((string?)m["ParameterName"]) is { } n && RefPath(m["ParameterValue"]) is { } path) vals[n] = path;
        }
    }

    // ------------------------------------------------------------ the car
    /// <summary>
    /// A body item's channels (Tier, Body Color, Painted, Decal, Decal Color,
    /// Wheels) and what the picks give (channel index -> option index; a
    /// channel not picked takes its default). skins and wheels are the
    /// registry's decal and wheel items.
    /// </summary>
    public async Task<CarPlan> PlanAsync(string bodyPackage, string bodyName, IReadOnlyList<CarItem> skins, IReadOnlyList<CarItem> wheels,
                                         IReadOnlyDictionary<int, int>? picks = null)
    {
        var plan = new CarPlan();
        var (p, exports) = await PackageAsync(bodyPackage, bodyName);

        var tiers = new List<Tier>();
        var tierDefault = 0;
        var colorVariants = new List<JObject>();
        var paintedVariants = new List<JObject>();
        foreach (var r in p["ItemVariants"] ?? new JArray())
        {
            var v = ExportOf(r, exports);
            if (v == null) continue;
            if (v["InlineVariant"]?["RichColorVar"] != null) { colorVariants.Add(v); continue; }
            if (v["GenericPropertyOptions"] is not JArray opts) continue;
            if (opts.Any(o => (o["CosmeticProperties"] ?? new JArray()).Any(cp => cp["TableRow"] != null))) { paintedVariants.Add(v); continue; }
            foreach (var o in opts)
            {
                var props = o["CosmeticProperties"] as JArray ?? [];
                var ams = props.Select(cp => RefPath(cp["AssembledMeshSchema"])).FirstOrDefault(x => x != null);
                if (ams == null) continue;
                if ((bool?)o["bIsDefault"] == true) tierDefault = tiers.Count;
                tiers.Add(new Tier(Text(o["VariantName"]) ?? "Tier " + (tiers.Count + 1), RefPath(o["PreviewImage"]), ams,
                                   props.Select(cp => (string?)cp["GameplayTag"]?["TagName"]).Where(t => t != null).Select(t => t!).ToList()));
            }
        }
        if (tiers.Count == 0 && RefPath(p["VehicleCosmeticsItemDef"]) is { } only)
            tiers.Add(new Tier("Default", null, only, []));
        if (tiers.Count == 0) return plan;

        // decals: CarSkin_* beside each tier's folder (/Skins/<Body>/<Tier>/)
        var decals = new List<Decal>();
        var bodyFolder = bodyPackage.Split('/').Reverse().Skip(1).FirstOrDefault();
        foreach (var t in tiers)
        {
            var tierFolder = t.Vcid.Split('/').Reverse().Skip(1).FirstOrDefault();
            var prefix = $"/VehicleCosmetics/Mutable/Skins/{bodyFolder}/{tierFolder}/";
            foreach (var s in skins.Where(a => a.Package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var (sp, sx) = await PackageAsync(s.Package, s.Name);
                    var vcid = RefPath(sp["VehicleCosmeticsItemDef"]);
                    var mat = vcid == null ? null : RefPath((await PropsAsync(vcid))["SkinMaterial"]);
                    if (mat == null) continue;
                    var color = (sp["ItemVariants"] ?? new JArray()).Select(r => ExportOf(r, sx)).FirstOrDefault(v => v?["InlineVariant"]?["RichColorVar"] != null);
                    var icon = (sp["DataList"] ?? new JArray()).Select(d => RefPath(d["Icon"])).FirstOrDefault(x => x != null);
                    decals.Add(new Decal(s.Title + (tiers.Count > 1 ? $" ({t.Name})" : ""), icon, t.Vcid, mat, color, vcid));
                }
                catch { /* a decal that won't read isn't offered */ }
            }
        }
        var wheelItems = wheels.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // ---- channels, in a fixed order (the picks are by index into it)
        var tierCh = new CarChannel { Name = "Tier", Default = tierDefault };
        tierCh.Options.AddRange(tiers.Select(t => new CarOption { Name = t.Name, Icon = t.Icon }));
        plan.Channels.Add(tierCh);
        var bodyCh = new CarChannel { Name = "Body Color" };
        plan.Channels.Add(bodyCh);
        var defaultPainted = PaintedOf(paintedVariants.FirstOrDefault(v => tiers[tierDefault].Tags.Contains((string?)v["VariantChannelTag"]?["TagName"] ?? ""))
                                       ?? paintedVariants.FirstOrDefault(), out var paintedDefault);
        CarChannel? paintedCh = null;
        if (defaultPainted.Count > 1)
        {
            paintedCh = new CarChannel { Name = "Painted", Default = paintedDefault };
            paintedCh.Options.AddRange(defaultPainted.Select(x => new CarOption { Name = x.Name, Icon = x.Icon }));
            plan.Channels.Add(paintedCh);
        }
        CarChannel? decalCh = null, decalColorCh = null;
        if (decals.Count > 0)
        {
            decalCh = new CarChannel { Name = "Decal" };
            decalCh.Options.Add(new CarOption { Name = "None" });
            decalCh.Options.AddRange(decals.Select(d => new CarOption { Name = d.Name, Icon = d.Icon }));
            plan.Channels.Add(decalCh);
            decalColorCh = new CarChannel { Name = "Decal Color" };
            plan.Channels.Add(decalColorCh);
        }
        var wheelCh = new CarChannel { Name = "Wheels" };
        plan.Channels.Add(wheelCh);
        int Pick(CarChannel? ch, int dflt) =>
            ch != null && picks != null && picks.TryGetValue(plan.Channels.IndexOf(ch), out var s) && s >= 0 ? s : dflt;

        // ---- the picks
        var dp = Pick(decalCh, 0);
        var decal = dp > 0 && dp <= decals.Count ? decals[dp - 1] : null;
        var tierPick = Math.Clamp(Pick(tierCh, tierDefault), 0, tiers.Count - 1);
        if (decal != null) tierPick = Math.Max(0, tiers.FindIndex(t => t.Vcid == decal.Tier));    // a decal fits its own tier's mesh
        var tier = tiers[tierPick];
        var vc = await PropsAsync(tier.Vcid);
        if (tierPick != tierDefault) plan.Styles.Add("Tier: " + tier.Name);

        var bodyColorVariant = colorVariants.FirstOrDefault(v => tier.Tags.Contains((string?)v["VariantChannelTag"]?["TagName"] ?? ""))
                               ?? colorVariants.FirstOrDefault(v => ((string?)v["VariantChannelTag"]?["TagName"] ?? "").Contains("Body.Color"));
        bodyCh.Options.AddRange(ColorChannel("Body Color", bodyColorVariant).Options);
        var bodyPick = Pick(bodyCh, 0);
        var bodyColor = SwatchColor(bodyColorVariant, bodyPick);
        if (bodyPick > 0 && bodyPick < bodyCh.Options.Count) plan.Styles.Add("Body Color: " + bodyCh.Options[bodyPick].Name);

        string? paintedRow = null;
        if (paintedCh != null)
        {
            var k = Math.Clamp(Pick(paintedCh, paintedDefault), 0, defaultPainted.Count - 1);
            paintedRow = defaultPainted[k].Row;      // the row names match across tiers
            if (k != paintedDefault) plan.Styles.Add("Painted: " + defaultPainted[k].Name);
        }

        double[]? decalColor = null;
        if (decalColorCh != null)
        {
            decalColorCh.Options.AddRange(ColorChannel("Decal Color", decal?.Color ?? decals.FirstOrDefault()?.Color).Options);
            if (decal != null)
            {
                plan.Styles.Add("Decal: " + decal.Name);
                decalColor = SwatchColor(decal.Color, Pick(decalColorCh, 0));
            }
        }

        // ---- the body, its material (the tier's, or the decal's)
        var mesh = RefPath(vc["SkeletalMeshInfo"]?["ParameterValue"]);
        if (mesh == null) return plan;
        plan.BodyMesh = mesh;
        var bodySlots = await SlotsAsync(mesh);
        var bodyMaterial = RefPath(vc["DefaultSkinMaterial"]);
        if (decal != null)
        {
            for (var i = 0; i < bodySlots.Count; i++)
                if (Same(bodySlots[i].Material, bodyMaterial)) plan.BodyOverrides[i] = decal.Material;
            bodyMaterial = decal.Material;
        }

        // ---- wheels: the picked wheel (else the tier's) on each wheel socket
        var stockVcid = RefPath(vc["DefaultWheelItemDef"]);
        var stockKey = stockVcid == null ? null : Tail(stockVcid)!.Split('.')[0].Replace("VCID_Wheel_", "Wheel_");
        var stockItem = stockKey == null ? null : wheelItems.FirstOrDefault(w => string.Equals(w.Name, stockKey, StringComparison.OrdinalIgnoreCase));
        wheelCh.Options.Add(new CarOption { Name = "Stock" + (stockItem != null ? " (" + stockItem.Title + ")" : ""), IconItem = stockItem });
        wheelCh.Options.AddRange(wheelItems.Select(w => new CarOption { Name = w.Title, IconItem = w }));
        var wheelPick = Pick(wheelCh, 0);
        var wheelVcid = stockVcid;
        if (wheelPick > 0 && wheelPick <= wheelItems.Count)
        {
            var wp = await PropsAsync(wheelItems[wheelPick - 1].ObjectPath);
            wheelVcid = RefPath(wp["VehicleCosmeticsItemDef"]) ?? stockVcid;
            plan.Styles.Add("Wheels: " + wheelItems[wheelPick - 1].Title);
        }
        var wv = wheelVcid == null ? null : await PropsAsync(wheelVcid);
        var wheelMesh = wv == null ? null : RefPath(wv["WheelSkeletalMeshInfo"]?["ParameterValue"]) ?? RefPath(wv["WheelStaticMeshInfo"]?["ParameterValue"]);
        if (wheelMesh != null)
        {
            plan.WheelMesh = wheelMesh;
            var setups = (wv!["WheelSetupInfos"] ?? new JArray()).ToDictionary(s => (string?)s["WheelLocation"] ?? "", s => s, StringComparer.OrdinalIgnoreCase);
            var sockets = await SocketsAsync(mesh, RefPath(vc["WheelAttachSkeletonReference"]));
            foreach (var at in vc["WheelAttachInfos"] ?? new JArray())
            {
                var where = (string?)at["WheelLocation"] ?? "";
                var sock = (bool?)at["WheelSocket"]?["bUseSocket"] == true
                    ? (string?)at["WheelSocket"]?["SocketReference"]?["SocketName"]
                    : (string?)at["WheelSocket"]?["BoneReference"]?["BoneName"];
                if (sock == null || !sockets.TryGetValue(sock, out var socketWorld)) continue;
                var local = Matrix4x4.Identity;
                if (setups.TryGetValue(where, out var su))
                {
                    var o = su["WheelOffset"]; var r = su["WheelRotation"]; var sc = su["WheelScale"];
                    var rot = new FRotator((float?)r?["Pitch"] ?? 0, (float?)r?["Yaw"] ?? 0, (float?)r?["Roll"] ?? 0).Quaternion();
                    local = Matrix4x4.CreateScale((float?)sc?["X"] ?? 1, (float?)sc?["Y"] ?? 1, (float?)sc?["Z"] ?? 1)
                            * Matrix4x4.CreateFromQuaternion(new Quaternion((float)rot.X, (float)rot.Y, (float)rot.Z, (float)rot.W))
                            * Matrix4x4.CreateTranslation((float?)o?["X"] ?? 0, (float?)o?["Y"] ?? 0, (float?)o?["Z"] ?? 0);
                }
                plan.Wheels.Add(("Wheel " + where.Split("::").Last(), local * socketWorld));
            }
        }

        // ---- the colours: Mutable's program, run with this car's values
        var coPath = RefPath(vc["CustomizableObject"]);
        if (coPath == null) return plan;
        MutableProgram mp;
        try { mp = await ProgramAsync(coPath); }
        catch { return plan; }      // no program: the materials keep their own values
        var vals = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Put(mp, vals, vc, "Body_");
        if (wv != null) Put(mp, vals, wv, "Wheel_");
        if (bodyColor != null && mp.Has("BodyColor")) vals["BodyColor"] = bodyColor;
        if (decalColor != null && mp.Has("SkinColor")) vals["SkinColor"] = decalColor;
        var paintedParam = (string?)vc["PaintedDataTableParameterName"] ?? "BodyPainted";
        if (paintedRow != null && mp.EnumValue(paintedParam, paintedRow) is { } pv) vals[paintedParam] = pv;
        if (decal?.Vcid != null && (string?)vc["SkinDataTableParameterName"] is { } skinTable)
        {
            var row = (string?)(await PropsAsync(decal.Vcid))["SkinRowName"];
            if (mp.EnumValue(skinTable, row) is { } sv) vals[skinTable] = sv;
        }
        if (bodyMaterial != null && mp.Has("BodyMaterial")) vals["BodyMaterial"] = bodyMaterial;

        List<MutableProgram.Surface> surfaces;
        try { surfaces = mp.Evaluate(vals); }
        catch { return plan; }
        var wheelSlots = wheelMesh == null ? [] : await SlotsAsync(wheelMesh);
        var label = string.Join(", ", plan.Styles.Select(x => x.Split(": ").Last()));
        foreach (var s in surfaces)
        {
            if (s.Vectors.Count + s.Scalars.Count == 0) continue;
            if (s.MaterialParam != null && s.MaterialParam.Contains("Overlay", StringComparison.OrdinalIgnoreCase)) continue;
            var slotName = s.Slot ?? s.Name;
            string? target = null;
            var bi = bodySlots.FindIndex(x => string.Equals(x.Slot, slotName, StringComparison.OrdinalIgnoreCase));
            if (bi >= 0) target = plan.BodyOverrides.GetValueOrDefault(bi) ?? bodySlots[bi].Material;
            else
            {
                var wi = wheelSlots.FindIndex(x => string.Equals(x.Slot, slotName, StringComparison.OrdinalIgnoreCase));
                if (wi >= 0) target = wheelSlots[wi].Material;
            }
            if (target == null) continue;
            // the surface's material must be the one the mesh has there (its own slot, the body
            // material, or the same asset): another (a mode's glass) isn't this car's
            if (s.Slot == null && !Same(s.Asset, target)) continue;
            if (!plan.Params.TryGetValue(Tail(target)!, out var ps))
            {
                ps = new ParamSet { Label = label };
                ps.Materials.Add(target);
                plan.Params[Tail(target)!] = ps;
            }
            foreach (var kv in s.Vectors) ps.Vectors.TryAdd(kv.Key, kv.Value);
            foreach (var kv in s.Scalars) ps.Scalars.TryAdd(kv.Key, kv.Value);
        }
        return plan;
    }

    /// <summary>A mesh's sockets (its own, else its skeleton's) in the mesh's space, UE units, row-vector matrices.</summary>
    private async Task<Dictionary<string, Matrix4x4>> SocketsAsync(string meshPath, string? skeletonPath)
    {
        var result = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        if (await provider.LoadPackageObjectAsync(meshPath) is not USkeletalMesh mesh) return result;
        var rs = mesh.ReferenceSkeleton;
        var global = new Matrix4x4[rs.FinalRefBonePose.Length];
        for (var i = 0; i < global.Length; i++)
        {
            var l = M(rs.FinalRefBonePose[i]);
            var parent = rs.FinalRefBoneInfo[i].ParentIndex;
            global[i] = parent >= 0 && parent < i ? l * global[parent] : l;
        }
        var socketObjs = new List<USkeletalMeshSocket>();
        foreach (var s in mesh.Sockets ?? [])
            if (s.Load<USkeletalMeshSocket>() is { } so) socketObjs.Add(so);
        if (skeletonPath != null && await provider.LoadPackageObjectAsync(skeletonPath) is USkeleton sk)
            foreach (var s in sk.Sockets ?? [])
                if (s.Load<USkeletalMeshSocket>() is { } so) socketObjs.Add(so);
        foreach (var so in socketObjs)
        {
            var name = so.SocketName.Text;
            if (result.ContainsKey(name)) continue;
            var bone = rs.FinalNameToIndexMap.TryGetValue(so.BoneName.Text, out var bi) ? global[bi] : Matrix4x4.Identity;
            var q = so.RelativeRotation.Quaternion();
            var local = Matrix4x4.CreateScale((float)so.RelativeScale.X, (float)so.RelativeScale.Y, (float)so.RelativeScale.Z)
                        * Matrix4x4.CreateFromQuaternion(new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W))
                        * Matrix4x4.CreateTranslation((float)so.RelativeLocation.X, (float)so.RelativeLocation.Y, (float)so.RelativeLocation.Z);
            result[name] = local * bone;
        }
        return result;
    }

    private static Matrix4x4 M(FTransform t) =>
        Matrix4x4.CreateScale((float)t.Scale3D.X, (float)t.Scale3D.Y, (float)t.Scale3D.Z)
        * Matrix4x4.CreateFromQuaternion(new Quaternion((float)t.Rotation.X, (float)t.Rotation.Y, (float)t.Rotation.Z, (float)t.Rotation.W))
        * Matrix4x4.CreateTranslation((float)t.Translation.X, (float)t.Translation.Y, (float)t.Translation.Z);
}
