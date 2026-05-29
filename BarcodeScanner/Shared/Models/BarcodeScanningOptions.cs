using BarcodeScanner.Models;

namespace BarcodeScanner.Shared.Models;

public sealed class BarcodeScanningOptions
{
    /// <summary>
    /// List of symbologies you want to detect. By default, all possible symbologies included.
    /// You can narrow down the list by setting this to your own list of symbologies.
    /// </summary>
    public IEnumerable<BarcodeSymbology>? PossibleFormats { get; set; } = [BarcodeSymbology.AllSymbologies];

    /// <summary>
    /// Delay in milliseconds. 1 second by default.
    /// If you are in continuous scanning mode use this to set delay between consecutive
    /// scans. 
    /// </summary>
    public int DelayBetweenContinuousScans { get; set; } = 1000;

    /// <summary>
    /// Delay in milliseconds. 150ms by default.
    /// Use this to tune delay between frame analysis
    /// </summary>
    public int DelayBetweenAnalyzingFrames { get; set; } = 150;

    /// <summary>
    /// Delay in milliseconds. 300ms by default.
    /// Use this to tune the delay before first frame will be analyzed.
    /// </summary>
    public int DelayBeforeAnalyzingFrames { get; set; } = 300;
    
    
}