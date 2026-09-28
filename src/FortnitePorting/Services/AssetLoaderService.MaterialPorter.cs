using FortnitePorting.Exporting.MaterialPorter;
using FortnitePorting.Models.Assets.Loading;

namespace FortnitePorting.Services;

/// <summary>Material Porter fork: Rocket Racing cars (their bodies), assembled with their wheels and paint.</summary>
public partial class AssetLoaderService
{
    public AssetLoaderService()
    {
        Categories.Add(new AssetLoaderCategory(EAssetCategory.RocketRacing)
        {
            Loaders =
            [
                new AssetLoader(EExportType.Car)
                {
                    ClassNames = [Cars.BodyClass],
                    HideRarity = true,
                }
            ]
        });
    }
}
