using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse_Conversion.Textures;
using FortnitePorting.Application;
using FortnitePorting.CUE4Parse.Extensions;
using FortnitePorting.Exporting.MaterialPorter;
using FortnitePorting.Extensions;
using FortnitePorting.MaterialPorter;
using Serilog;
using SkiaSharp;

namespace FortnitePorting.Models.Assets.Asset;

/// <summary>
/// Material Porter fork: a car's styles. Its channels (Tier, Body Color,
/// Painted, Decal, Decal Color, Wheels) aren't item variants the game lists:
/// they come from Material Porter's car assembly (Cars.PlanAsync), built off
/// the UI thread; option previews (tier and decal images, colour swatches,
/// wheel icons) fill in after.
/// A LEGO figure's expression: the face rig's pose of its mouth, eyes and brows.
/// </summary>
public partial class AssetInfo
{
    private void AddCarStyles()
    {
        var path = Asset.CreationData.Object.GetPathName();
        _ = Task.Run(async () =>
        {
            CarPlan plan;
            try { plan = await MaterialPorterService.Instance.CarPlanAsync(path, null); }
            catch (Exception e)
            {
                Log.Warning(e, "[Material Porter] car styles for {Path}", path);
                return;
            }

            var infos = new List<AssetStyleInfo>();
            var previews = new List<(CarStyleData Data, CarOption Option)>();
            for (var c = 0; c < plan.Channels.Count; c++)
            {
                var channel = plan.Channels[c];
                var datas = new List<CarStyleData>();
                for (var o = 0; o < channel.Options.Count; o++)
                {
                    var data = new CarStyleData(channel.Options[o].Name, c, o);
                    datas.Add(data);
                    previews.Add((data, channel.Options[o]));
                }
                if (datas.Count == 0) continue;
                infos.Add(new AssetStyleInfo(channel.Name, datas)
                {
                    SelectedStyleIndex = Math.Clamp(channel.Default, 0, datas.Count - 1),
                    // a long list (the wheel sets): a searchable tile grid
                    IsPicker = datas.Count > 30,
                });
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var info in infos) StyleInfos.Add(info);
            });

            foreach (var (data, option) in previews)
            {
                try
                {
                    if (Preview(option) is { } bitmap)
                        await Dispatcher.UIThread.InvokeAsync(() => data.StyleDisplayImage = bitmap);
                }
                catch { /* no preview for that one */ }
            }
        });
    }

    const string FaceSettings = "/FigureCharacter/Figure_Core/Rig/DA_Figure_Face_Settings.DA_Figure_Face_Settings";
    const string MouthAtlas = "/FigureCharacter/Figure_Core/Texture/Face/Mouth/T_Atlas_Figure_Mouth_Thin.T_Atlas_Figure_Mouth_Thin";
    const string BrowAtlas = "/FigureCharacter/Figure_Core/Texture/Face/Brow/T_Atlas_Figure_Brow_Thin01.T_Atlas_Figure_Brow_Thin01";

    /// <summary>
    /// A LEGO figure's expression channels: Mouth, Eyes, Brows, each the figure's own or one of
    /// the face rig's poses (DA_Figure_Face_Settings has a row per pose: 46 mouths, 12 brows,
    /// 6 eyes). The mouths' and brows' previews are their cells of the default atlases
    /// (7 x 7 mouths, 4 x 4 brows), drawn as the face prints them.
    /// </summary>
    private void AddFigureFaceStyles()
    {
        _ = Task.Run(async () =>
        {
            var provider = AppServices.UEParse.Provider!;
            int mouths = 46, brows = 12, eyes = 6;
            if (provider.TryLoadPackageObject(FaceSettings, out var settings))
            {
                if (settings.GetOrDefault("Mouth Pose Matrix", Array.Empty<FStructFallback>()).Length is > 0 and var m) mouths = m;
                if (settings.GetOrDefault("Brow Pose Matrix", Array.Empty<FStructFallback>()).Length is > 0 and var b) brows = b;
                if (settings.GetOrDefault("Show Eyelash", Array.Empty<bool>()).Length is > 0 and var e) eyes = e;
            }
            var channels = new (string Feature, int Count, string? Atlas, int Grid)[] { ("Mouth", mouths, MouthAtlas, 7), ("Eyes", eyes, null, 0), ("Brows", brows, BrowAtlas, 4) };
            var infos = new List<(AssetStyleInfo Info, List<FigureFaceStyleData> Datas, string? Atlas, int Grid)>();
            foreach (var (feature, count, atlas, grid) in channels)
            {
                var datas = new List<FigureFaceStyleData> { new("Figure's own", feature, -1) };
                for (var pose = 0; pose < count; pose++) datas.Add(new FigureFaceStyleData($"{feature} {pose}", feature, pose));
                infos.Add((new AssetStyleInfo(feature, datas) { IsPicker = datas.Count > 30 }, datas, atlas, grid));
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var (info, _, _, _) in infos) StyleInfos.Add(info);
            });

            foreach (var (_, datas, atlas, grid) in infos)
            {
                if (atlas is null) continue;
                try
                {
                    if (!provider.TryLoadPackageObject<UTexture2D>(atlas, out var texture) || texture.Decode()?.ToSkBitmap() is not { } sheet) continue;
                    using (sheet)
                        foreach (var data in datas.Where(d => d.Pose >= 0))
                        {
                            var bitmap = FaceCell(sheet, grid, data.Pose, data.Feature == "Brows");
                            await Dispatcher.UIThread.InvokeAsync(() => data.StyleDisplayImage = bitmap);
                        }
                }
                catch (Exception e)
                {
                    Log.Warning(e, "[Material Porter] figure expression previews");
                }
            }
        });
    }

    /// <summary>
    /// A face atlas cell, drawn on LEGO yellow: a mouth's outline (B only) black, its lips (R only)
    /// red, its inside (G and B) dark, its teeth (all three) white; a brow (any channel) black.
    /// </summary>
    private static Avalonia.Media.Imaging.WriteableBitmap FaceCell(SKBitmap sheet, int grid, int index, bool brow)
    {
        const int size = 96;
        var cell = sheet.Width / grid;
        var x0 = index % grid * cell;
        var y0 = index / grid * cell;
        using var icon = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var colour = new float[3];
        void Over(float amount, float r, float g, float b)
        {
            colour[0] += (r - colour[0]) * amount;
            colour[1] += (g - colour[1]) * amount;
            colour[2] += (b - colour[2]) * amount;
        }
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var c = sheet.GetPixel(x0 + x * cell / size, y0 + y * cell / size);
            float r = c.Red / 255f, g = c.Green / 255f, b = c.Blue / 255f;
            colour[0] = 0.96f; colour[1] = 0.80f; colour[2] = 0.22f;
            if (brow) Over(Math.Max(r, Math.Max(g, b)), 0.05f, 0.04f, 0.03f);
            else
            {
                Over(b, 0.05f, 0.04f, 0.03f);
                Over(r * (1 - g), 0.72f, 0.13f, 0.12f);
                Over(g * (1 - r), 0.22f, 0.03f, 0.03f);
                Over(Math.Min(r, g), 0.97f, 0.97f, 0.95f);
            }
            icon.SetPixel(x, y, new SKColor((byte)(colour[0] * 255), (byte)(colour[1] * 255), (byte)(colour[2] * 255)));
        }
        return icon.ToWriteableBitmap();
    }

    /// <summary>An option's preview: its image, a swatch of its colour, or its item's icon.</summary>
    private static Avalonia.Media.Imaging.WriteableBitmap? Preview(CarOption option)
    {
        var provider = AppServices.UEParse.Provider!;
        if (option.Swatch is { Length: >= 6 } hex && SKColor.TryParse("#" + hex[..6], out var color))
        {
            using var swatch = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            swatch.Erase(color);
            return swatch.ToWriteableBitmap();
        }
        UTexture2D? texture = null;
        if (option.Icon is { } icon) provider.TryLoadPackageObject(icon, out texture);
        if (texture is null && option.IconItem is { } item && provider.TryLoadPackageObject(item.ObjectPath, out var itemObject))
            texture = itemObject.GetDataListItem<UTexture2D>("Icon", "LargeIcon")
                      ?? itemObject.GetAnyOrDefault<UTexture2D?>("SmallPreviewImage", "LargePreviewImage", "Icon", "LargeIcon");
        return texture?.Decode()?.ToWriteableBitmap();
    }
}
