using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FortnitePorting.Extensions;

namespace FortnitePorting.MaterialPorter;

/// <summary>
/// Material Porter fork: an export type's icon (Assets/FN/&lt;type&gt;.png). A type without one of its
/// own takes the plain icon: a missing resource threw while the Assets page was being built, and
/// the page never came up (the fork's Effects and Contrails tabs had none).
/// </summary>
public static class TabIcons
{
    private const string Plain = "avares://FortnitePorting/Assets/FN/Misc.png";

    public static Bitmap Of(EExportType type)
    {
        var own = $"avares://FortnitePorting/Assets/FN/{type}.png";
        return ImageExtensions.AvaresBitmap(AssetLoader.Exists(new Uri(own)) ? own : Plain);
    }
}
