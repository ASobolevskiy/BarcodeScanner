namespace BarcodeScanner.Models;

public enum BarcodeSymbology
{
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
    /// <summary>
    /// Use this if you want to include all symbologies at once
    /// </summary>
    AllSymbologies
}