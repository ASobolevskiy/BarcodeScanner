using BarcodeScanner;
using BarcodeScanner.Models;

namespace BarcodeScanner.Enums;

/// <summary>
/// The outcome of a scan, reported via <see cref="BarcodeResult.Status"/>.
/// </summary>
public enum ScanStatus
{
    /// <summary>
    /// A barcode was decoded. <see cref="BarcodeResult.RawValue"/>, <see cref="BarcodeResult.DisplayValue"/>,
    /// <see cref="BarcodeResult.Symbology"/>, and <see cref="BarcodeResult.ScannedTime"/> are populated.
    /// </summary>
    Success,

    /// <summary>
    /// The scan ended because <see cref="IMobileBarcodeScanner.CancelScan"/> was called, or the
    /// user dismissed the scanner (e.g. tapped back) before a barcode was decoded.
    /// </summary>
    CancelledByUser,

    /// <summary>
    /// The scan ended automatically because <see cref="BarcodeScanningOptions.UseAutoClose"/>
    /// was enabled and the configured delay elapsed before a barcode was decoded. Only
    /// occurs for single scans — see <see cref="BarcodeScanningOptions.UseAutoClose"/> for details.
    /// </summary>
    AutoClosed,

    /// <summary>
    /// The scan could not be started or failed, for a reason described in
    /// <see cref="BarcodeResult.ErrorMessage"/> — e.g. a setup failure, an invalid
    /// configuration, or a scan already in progress on the same instance.
    /// </summary>
    Error
}
