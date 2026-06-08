using BarcodeScanner.Models;

namespace BarcodeScanner;

public interface IMobileBarcodeScanner
{
    Task<BarcodeResult?> ScanAsync(BarcodeScanningOptions? options = null);
    
    Task ScanContinuouslyAsync(
        BarcodeScanningOptions? options,
        Action<BarcodeResult?> onResult);

    void CancelScan();
    void ToggleTorch();
}