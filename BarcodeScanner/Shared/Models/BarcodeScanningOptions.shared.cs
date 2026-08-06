using System;
using System.Collections.Generic;
using BarcodeScanner;

namespace BarcodeScanner.Models;

/// <summary>
/// Configuration for a single scan session, passed to <see cref="IMobileBarcodeScanner.ScanAsync"/>
/// or <see cref="IMobileBarcodeScanner.ScanContinuouslyAsync"/>. If omitted, default values are used.
/// </summary>
public sealed class BarcodeScanningOptions
{
    /// <summary>
    /// List of symbologies you want to detect. By default, all possible symbologies included.
    /// You can narrow down the list by setting this to your own list of symbologies.
    /// </summary>
    public IEnumerable<BarcodeSymbology> PossibleFormats { get; set; } = [BarcodeSymbology.AllSymbologies];

    /// <summary>
    /// Delay in milliseconds. 1 second by default.
    /// If you are in continuous scanning mode use this to set delay between consecutive
    /// scans. 
    /// </summary>
    public int DelayBetweenContinuousScans { get; set; } = 1000;

    /// <summary>
    /// Delay in milliseconds. 150ms by default.
    /// Use this to tune delay between frame analysis
    /// </summary>
    public int DelayBetweenAnalyzingFrames { get; set; } = 150;

    /// <summary>
    /// Delay in milliseconds. 300ms by default.
    /// Use this to tune the delay before first frame will be analyzed.
    /// </summary>
    public int DelayBeforeAnalyzingFrames { get; set; } = 300;
    
    /// <summary>
    /// Factory for creating a custom scanner overlay. Called once per scan session, on the UI
    /// thread, during scanner setup. Must return a new <c>Android.Views.View</c> or
    /// <c>UIKit.UIView</c> instance — the platform context (<c>Activity</c> on Android,
    /// <c>UIViewController</c> on iOS) is passed as the argument so you don't need to capture
    /// it from elsewhere. Implement <see cref="IActiveScannerOverlay"/> on the returned view if
    /// you want it to receive live updates from the scan engine (detected barcode positions,
    /// region-of-interest sync, reset notifications) — otherwise it's shown but never updated.
    /// </summary>
    /// <remarks>
    /// ⚠️ WARNING: return a fresh instance on every call — never a cached/reused one.
    /// <list type="bullet">
    /// <item>The library removes the returned view from its parent when the scan session ends,
    /// but does not dispose it — ownership and lifetime are yours.</item>
    /// <item><b>Android:</b> the view holds the passed <c>Activity</c> as its <c>Context</c>
    /// for as long as the view itself is alive — this is normal and unavoidable (every Android
    /// <c>View</c> does this), but it means caching the view for reuse keeps that specific
    /// <c>Activity</c> instance alive for as long as the cache does.</item>
    /// <item><b>iOS:</b> do not store the passed <c>UIViewController</c> anywhere that outlives
    /// this call (a field, a closure) — a saved reference is never garbage-collected, because
    /// the .NET-for-iOS runtime cannot collect a reference cycle that crosses a
    /// natively-retained object. This is not a slow leak — it never resolves.</item>
    /// </list>
    /// </remarks>
    public Func<object, object>? CustomOverlayFactory { get; set; }

    /// <summary>
    /// Delay before scanner closure after successful scan (in milliseconds)
    /// </summary>
    /// <remarks>
    /// ⚠️ WARNING: Minimal acceptable value is <b>300 ms</b>.
    /// If you try to set value below 300ms it will be automatically set
    /// to 300ms to ensure there will be no problems with animations.
    /// </remarks>
    public int DelayBeforeScannerClose
    {
        get;
        set => field = Math.Max(300, value);
    } = 500;
    
    /// <summary>
    /// Set if you want to enable automatic scanner closure.
    /// <remarks>
    /// This works only in single scan mode. Continuous scan mode does not use this feature.
    /// </remarks>
    /// </summary>
    public bool UseAutoClose { get; set; } = false;
    
    /// <summary>
    /// Delay (in seconds) before scanner will auto close. 15 seconds by default.
    /// <remarks>
    /// If you set <b>UseAutoClose = true</b> and <b>AutoCloseDelaySeconds = 0</b> automatic closure will not work.
    /// </remarks>
    /// </summary>
    public int AutoCloseDelaySeconds { get; set; } = 15;
    
    /// <summary>
    /// Region of Interest in relative coordinates (from 0.0 to 1.0).
    /// If null, the viewfinder rectangle from the overlay is used (or the entire screen if the overlay is custom).
    /// </summary>
    public RoiRect? RegionOfInterest { get; set; }

    /// <summary>
    /// Delay before the overlay visually returns to its default (idle) state after a successful
    /// detection in continuous scan mode (in milliseconds). Not used in single-shot mode — use
    /// <see cref="DelayBeforeScannerClose"/> for that.
    /// </summary>
    /// <remarks>
    /// ⚠️ WARNING: The effective value used at scan time will never exceed
    /// <see cref="DelayBetweenContinuousScans"/> — if you set this higher, it will be silently
    /// clamped down to <see cref="DelayBetweenContinuousScans"/> when the scan starts, so the
    /// overlay always has time to finish resetting before the next detection is allowed.
    /// Setting this property itself is not clamped immediately (unlike <see cref="DelayBeforeScannerClose"/>) —
    /// the clamp is applied when scanning starts, not when this value is assigned.
    /// </remarks>
    public int DelayBeforeOverlayReset { get; set; } = 500;

    internal ScanType ScannerMode { get; set; }

    /// <summary>
    /// Shallow copy used internally so the library never mutates an options instance owned by the caller.
    /// </summary>
    internal BarcodeScanningOptions Clone() => (BarcodeScanningOptions)MemberwiseClone();

    /// <summary>
    /// Single source of truth for the DelayBeforeOverlayReset/DelayBetweenContinuousScans invariant.
    /// Computed at point of use (not in a property setter) since a cross-field clamp in an object
    /// initializer setter would depend on property assignment order.
    /// </summary>
    internal int GetEffectiveOverlayResetDelay() =>
        Math.Max(0, Math.Min(DelayBeforeOverlayReset, DelayBetweenContinuousScans));
}

public enum ScanType
{
    OneShot,
    Continuous
}

