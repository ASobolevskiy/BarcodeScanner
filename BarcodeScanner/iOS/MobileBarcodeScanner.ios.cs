using BarcodeScanner.Models;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner
{
    private partial Task<BarcodeResult?> PlatformScanSingleAsync(BarcodeScanningOptions options)
    {
        return Task.FromResult(default(BarcodeResult?));
    }

    private partial Task PlatformScanContinuousAsync(BarcodeScanningOptions options)
    {
        return Task.CompletedTask;
    }

    private partial void PlatformStopContinuousScan()
    {
        
    }
}