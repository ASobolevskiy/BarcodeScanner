using System;
using System.Threading.Tasks;
using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace BarcodeScanner;

/// <summary>
/// Cross-platform contract for scanning barcodes/QR codes via the device camera.
/// Call <see cref="ScanAsync"/> for a single scan or <see cref="ScanContinuouslyAsync"/>
/// for continuous scanning.
/// </summary>
public interface IMobileBarcodeScanner
{
    /// <summary>
    /// Presents a full-screen scanner and completes with a single scan result.
    /// </summary>
    /// <param name="options">Scanning options. If null, default options are used.</param>
    /// <returns>
    /// The scan outcome — always inspect <see cref="BarcodeResult.Status"/>; the task never
    /// completes with a null result, including for errors, cancellation, or auto-close.
    /// Calling this while a scan is already in progress on the same instance completes
    /// immediately with <see cref="ScanStatus.Error"/> instead of starting a second scan.
    /// Awaiting from the UI thread resumes on the UI thread, as with any awaited <see cref="Task"/>.
    /// </returns>
    Task<BarcodeResult> ScanAsync(BarcodeScanningOptions? options = null);

    /// <summary>
    /// Presents a full-screen scanner that keeps scanning and invokes <paramref name="onResult"/>
    /// for every detected barcode until <see cref="CancelScan"/> is called or the scanner is
    /// dismissed by the user.
    /// </summary>
    /// <param name="onResult">
    /// Invoked on every detection and never with null. Also invoked once with
    /// <see cref="ScanStatus.Error"/> if a scan is already in progress on the same instance,
    /// instead of starting a second scan. Always invoked on the main/UI thread — do not run
    /// blocking or long-running work directly inside it, as that delays the camera preview and
    /// overlay rendering; dispatch such work elsewhere yourself.
    /// </param>
    /// <param name="options">Scanning options. If null, default options are used.</param>
    /// <returns>A task that completes when the continuous scan session ends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onResult"/> is null.</exception>
    Task ScanContinuouslyAsync(
        Action<BarcodeResult> onResult,
        BarcodeScanningOptions? options = null);

    /// <summary>
    /// Cancels the in-progress scan started by <see cref="ScanAsync"/> or
    /// <see cref="ScanContinuouslyAsync"/> and dismisses the scanner screen.
    /// </summary>
    /// <remarks>Does nothing if no scan is currently in progress on this instance.</remarks>
    void CancelScan();

    /// <summary>
    /// Toggles the camera torch (flashlight) on or off.
    /// </summary>
    /// <remarks>Has no effect outside an active scan session.</remarks>
    void ToggleTorch();
}