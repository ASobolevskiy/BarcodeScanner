using BarcodeScanner.Models;

namespace BarcodeScanner;

internal readonly record struct ScannerRegistration(
    WeakReference<MobileBarcodeScanner>  Scanner,
    TaskCompletionSource<BarcodeResult?>? SingleScanTcs,
    TaskCompletionSource<bool>? ContinuousScanTcs,
    BarcodeScanningOptions Options);