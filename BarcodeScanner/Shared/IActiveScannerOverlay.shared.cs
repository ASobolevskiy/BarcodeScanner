using BarcodeScanner.Models;

namespace BarcodeScanner;

public interface IActiveScannerOverlay
{
    void ClearOverlay();
    void UpdateOverlay(string? barcodeValue, float[] targetPoints);
    
    /// <summary>
    /// The barcode detection area (in px) drawn by this overlay
    /// <remarks>Library uses this value if the BarcodeScanningOptions.RegionOfInterest is not set</remarks>
    /// </summary>
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