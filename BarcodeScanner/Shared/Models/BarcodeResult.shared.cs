using System;
using BarcodeScanner;
using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace BarcodeScanner.Models;

/// <summary>
/// The outcome of a scan — returned from <see cref="IMobileBarcodeScanner.ScanAsync"/>
/// and passed to the <c>onResult</c> callback of <see cref="IMobileBarcodeScanner.ScanContinuouslyAsync"/>.
/// Always inspect <see cref="Status"/> first — a successfully decoded barcode is only one
/// of several possible outcomes.
/// </summary>
public sealed record BarcodeResult
{
    /// <summary>
    /// The result of the scan — success, error, or how the session ended if no barcode was
    /// decoded (cancelled by the user, or auto-closed). This is the primary field to check
    /// before reading any other property; see <see cref="IsSuccessfulScan"/> and
    /// <see cref="IsCancelled"/> for convenience checks.
    /// </summary>
    public ScanStatus Status { get; init; } = ScanStatus.Error;

    /// <summary>
    /// A human-readable error description. Populated when <see cref="Status"/> is
    /// <see cref="ScanStatus.Error"/>; null otherwise.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The detected barcode format. <see cref="BarcodeSymbology.Unknown"/> unless
    /// <see cref="Status"/> is <see cref="ScanStatus.Success"/>.
    /// </summary>
    public BarcodeSymbology Symbology { get; init; } = BarcodeSymbology.Unknown;

    /// <summary>
    /// The raw decoded barcode content. Populated when <see cref="Status"/> is
    /// <see cref="ScanStatus.Success"/>; empty string otherwise.
    /// </summary>
    public string RawValue { get; init; } = string.Empty;

    /// <summary>
    /// A display-friendly version of the decoded content, which may differ from
    /// <see cref="RawValue"/> depending on the barcode format. Populated when
    /// <see cref="Status"/> is <see cref="ScanStatus.Success"/>; empty string otherwise.
    /// </summary>
    public string DisplayValue { get; init; } = string.Empty;

    /// <summary>
    /// When the barcode was scanned. Null unless <see cref="Status"/> is
    /// <see cref="ScanStatus.Success"/>.
    /// </summary>
    public DateTimeOffset? ScannedTime { get; init; }

    /// <summary>
    /// <see langword="true"/> if <see cref="Status"/> is <see cref="ScanStatus.Success"/>.
    /// </summary>
    public bool IsSuccessfulScan => Status is ScanStatus.Success;

    /// <summary>
    /// <see langword="true"/> if the scan ended without an error — either the user cancelled it
    /// or it was closed automatically (see <see cref="BarcodeScanningOptions.UseAutoClose"/>).
    /// </summary>
    public bool IsCancelled => Status is ScanStatus.AutoClosed or ScanStatus.CancelledByUser;

    public override string ToString()
    {
        return $"Format: {Symbology.ToString()}, RawValue: {RawValue}, DisplayValue: {DisplayValue}";
    }
}