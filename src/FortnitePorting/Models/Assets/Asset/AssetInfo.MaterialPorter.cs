using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CUE4Parse.UE4.Assets.Exports.Texture;
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
