using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FortnitePorting.Exporting.MaterialPorter;
using FortnitePorting.Models.Assets.Asset;
using Serilog;

namespace FortnitePorting.Models.Assets.Loading;

/// <summary>
/// Material Porter fork: assets listed unread, the asset registry's and others. The cooked registry
/// keeps few of the game's own Niagara systems (the Effects tab listed 1,379, nearly all islands');
/// the others are only reachable by file path, and holding them all read took over 40 GB.
/// </summary>
public partial class AssetLoader
{
    /// <summary>
    /// The packages to list, given the registry's entries of this loader's classes: (package path,
    /// object name). Each one whose object's class (read from the package's export map, with an
    /// outline of the system: Effects.Outline) is one of <see cref="ClassNames"/> is listed unread
    /// (<see cref="Unloaded"/>): the export reads it. The registry's entries aren't listed otherwise.
    /// </summary>
    public Func<IReadOnlyList<(string Package, string Name)>, IReadOnlyList<(string Package, string Name)>>? MPUnregistered;

    private async Task LoadUnregistered(IReadOnlyList<(string Package, string Name)> packages, CancellationToken token)
    {
        if (packages.Count == 0) return;
        int start = LoadedAssets, done = 0;
        await Parallel.ForEachAsync(packages, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
            async (package, ct) =>
            {
                if (token.IsCancellationRequested) return;
                await WaitIfPausedAsync();
                try
                {
                    if (await Effects.ReadOutline(UEParse.Provider, package.Package, package.Name) is { Class: { } className } outline
                        && ClassNames.Contains(className))
                        await LoadAsset(Unloaded.Create(package.Package, package.Name, className, outline));
                }
                catch (Exception e)
                {
                    Log.Error("{0}", e);
                }

                LoadedAssets = start + Interlocked.Increment(ref done);
            });
    }
}
