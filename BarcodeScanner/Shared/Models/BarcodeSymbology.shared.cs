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
    /// Do not set this explicitly. If this value will be in possible formats 
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