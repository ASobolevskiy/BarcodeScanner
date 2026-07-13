using BarcodeScanner.Models;

namespace BarcodeScanner;

internal readonly record struct ScannerRegistration(
    MobileBarcodeScanner Scanner,
    IPlatformScannerSession? PlatformSession,
    BarcodeScanningOptions Options);