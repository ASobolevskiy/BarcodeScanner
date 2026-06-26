namespace BarcodeScanner.Models;

internal readonly record struct DetectionResult
{
    public string? RawValue { get; init; }
    public string? DisplayValue { get; init; }
    public BarcodeSymbology Symbology { get; init; }
    public float[]? SmoothedPoints { get; init; }
    public bool ShouldResetOverlay { get; init; }
    public ScanType ScanType { get; init; }
}

internal readonly record struct BarcodeData(
    string? RawValue,
    string? DisplayValue,
    BarcodeSymbology Symbology,
    float[] ScreenCornerPoints);