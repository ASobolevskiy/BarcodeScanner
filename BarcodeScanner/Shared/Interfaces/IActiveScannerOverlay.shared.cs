using BarcodeScanner.Models;

namespace BarcodeScanner;

/// <summary>
/// Implement this on a custom overlay view (returned from
/// <see cref="BarcodeScanningOptions.CustomOverlayFactory"/>) to receive live updates from
/// the scan engine — detected barcode positions, region-of-interest sync, and reset
/// notifications. The library's built-in overlays implement this interface internally.
/// </summary>
/// <remarks>
/// ⚠️ WARNING: see <see cref="BarcodeScanningOptions.CustomOverlayFactory"/> for the ownership
/// contract — the library never disposes a custom overlay, and the factory must return a fresh
/// instance per scan session, not a cached/reused one.
/// </remarks>
public interface IActiveScannerOverlay
{
    /// <summary>
    /// Called to reset the overlay to its idle (no detection) visual state — after the
    /// <see cref="BarcodeScanningOptions.DelayBeforeOverlayReset"/> following a detection in
    /// continuous mode, or whenever a frame has nothing to highlight.
    /// </summary>
    void ClearOverlay();

    /// <summary>
    /// Called on every frame where a barcode is detected and being tracked, to draw or
    /// animate a highlight around it.
    /// </summary>
    /// <param name="barcodeValue">The decoded value of the tracked barcode.</param>
    /// <param name="targetPoints">
    /// The four corner points of the detected barcode (top-left, top-right, bottom-right,
    /// bottom-left, each as an x,y pair — 8 floats total), in the same view coordinate
    /// space the overlay is drawn in.
    /// </param>
    void UpdateOverlay(string? barcodeValue, float[] targetPoints);

    /// <summary>
    /// The barcode detection area (in px) drawn by this overlay.
    /// </summary>
    /// <remarks>
    /// Library uses this value if the <see cref="BarcodeScanningOptions.RegionOfInterest"/> is not set.
    /// </remarks>
    ViewFinderRect GetViewfinderRect();

    /// <summary>
    /// This will by called once on scanning start if the BarcodeScanningOptions.RegionOfInterest is set
    /// so the default overlays could sync their animated frames to respect set value.
    /// Custom overlays may not override this - then visual frame and actual ROI can mismatch
    /// </summary>
    void SyncRegionOfInterest(RoiRect roi)
    {
    }
}