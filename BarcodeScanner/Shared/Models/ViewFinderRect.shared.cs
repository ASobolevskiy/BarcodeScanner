namespace BarcodeScanner.Models;

/// <summary>
/// The barcode detection area drawn by a scanner overlay, in pixels within that overlay's own
/// view coordinate space — (0, 0) is the top-left corner. Returned from
/// <see cref="IActiveScannerOverlay.GetViewFinderRect"/>; the library falls back to this value
/// as the detection area whenever <see cref="BarcodeScanningOptions.RegionOfInterest"/> is not
/// set. Unlike <see cref="RoiRect"/>, coordinates here are absolute pixels, not relative
/// (0.0–1.0) fractions of the screen.
/// </summary>
/// <param name="Left">Left edge, in pixels.</param>
/// <param name="Top">Top edge, in pixels.</param>
/// <param name="Right">Right edge, in pixels.</param>
/// <param name="Bottom">Bottom edge, in pixels.</param>
public readonly record struct ViewFinderRect(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    /// <summary>Width of the rect, in pixels.</summary>
    public float Width => Right - Left;

    /// <summary>Height of the rect, in pixels.</summary>
    public float Height => Bottom - Top;

    /// <summary>A zero-sized rect at the origin.</summary>
    public static readonly ViewFinderRect Empty = new(0, 0, 0, 0);
}