using BarcodeScanner.Models;

namespace BarcodeScanner.Shared.Models;

public sealed class BarcodeResult
{
    public BarcodeSymbology Symbology { get; set; }
    public string? RawValue { get; set; }
    public string? DisplayValue { get; set; }
    public Point[]? CornerPoints { get; set; }
}