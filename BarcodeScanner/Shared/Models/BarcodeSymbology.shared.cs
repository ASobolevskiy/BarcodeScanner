namespace BarcodeScanner.Models;

/// <summary>
/// Barcode/QR symbologies (formats) this library can detect. Used to narrow detection via
/// <see cref="BarcodeScanningOptions.PossibleFormats"/> and reported on a successful scan
/// via <see cref="BarcodeResult.Symbology"/>. The concrete values represent the intersection
/// of what both platforms' native scanning engines support.
/// </summary>
public enum BarcodeSymbology
{
    /// <summary>
    /// Do not set this explicitly — it has no native meaning and cannot be scanned for. If
    /// <c>PossibleFormats</c> contains it alongside other symbologies, it is silently removed
    /// (logged as a warning); if it is the only value present, <c>ScanAsync</c>/
    /// <c>ScanContinuouslyAsync</c> returns <c>ScanStatus.Error</c> instead of starting a scan.
    /// </summary>
    Unknown,
    Aztec,
    Code128,
    Code39,
    Code93,
    Ean13,
    Ean8,
    QrCode,
    Upce,
    Itf,
    DataMatrix,
    Pdf417,
    Codabar,
    /// <summary>
    /// Use this if you want to include all symbologies at once
    /// </summary>
    AllSymbologies
}