namespace BarcodeScanner.Models;

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