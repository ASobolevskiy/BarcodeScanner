using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace BarcodeScanner.Models;

public sealed record BarcodeResult
{
    public ScanStatus Status { get; init; } = ScanStatus.Error;
    public string? ErrorMessage { get; init; }
    public BarcodeSymbology Symbology { get; init; } = BarcodeSymbology.Unknown;
    public string? RawValue { get; init; } = string.Empty;
    public string? DisplayValue { get; init; } = string.Empty;
    public string? ScannedTime { get; init; } = string.Empty;
    public bool IsSuccessfulScan => Status is ScanStatus.Success;
    public bool IsCancelled => Status is ScanStatus.AutoClosed or ScanStatus.CancelledByUser;

    public override string ToString()
    {
        return $"Format: {Symbology.ToString()}, RawValue: {RawValue}, DisplayValue: {DisplayValue}";
    }
}