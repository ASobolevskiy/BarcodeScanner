namespace BarcodeScanner;

public interface IActiveScannerOverlay
{
    void ClearOverlay();
    void UpdateOverlay(string? barcodeValue, float[] targetPoints);
}