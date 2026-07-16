using System;
using System.Linq;
using BarcodeScanner.Models;

namespace BarcodeScanner.Helpers;

/// <summary>
/// All concrete <see cref="BarcodeSymbology"/> values mapped to a native platform type,
/// i.e. every symbology except the sentinel <see cref="BarcodeSymbology.Unknown"/> and
/// <see cref="BarcodeSymbology.AllSymbologies"/> values.
/// </summary>
/// <remarks>
/// Used by both platforms to expand "all formats" into an explicit list instead of relying
/// on a platform-specific wildcard (ML Kit's FormatAllFormats has no iOS equivalent and would
/// pull in symbologies AVFoundation can't distinguish, e.g. UPC-A). Expanding explicitly keeps
/// detection limited to the actual ML Kit / AVFoundation intersection on both platforms.
/// </remarks>
internal static class BarcodeSymbologySet
{
    internal static readonly BarcodeSymbology[] AllConcrete =
        Enum.GetValues<BarcodeSymbology>()
            .Where(s => s is not (BarcodeSymbology.Unknown or BarcodeSymbology.AllSymbologies))
            .ToArray();
}
