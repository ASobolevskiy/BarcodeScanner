using Android.App;
using Android.Views;

namespace BarcodeScanner.Models;

public partial class BarcodeScanningOptions
{
    /// <summary>
    /// Factory for creating a custom scanner overlay. Called once per scan session, on the UI
    /// thread, during scanner setup. Must return a new <see cref="View"/> instance — the
    /// current <see cref="Activity"/> is passed as the argument so you don't need to capture
    /// it from elsewhere. Implement <see cref="IActiveScannerOverlay"/> on the returned view if
    /// you want it to receive live updates from the scan engine (detected barcode positions,
    /// region-of-interest sync, reset notifications) — otherwise it's shown but never updated.
    /// </summary>
    /// <remarks>
    /// ⚠️ WARNING: return a fresh instance on every call — never a cached/reused one.
    /// <list type="bullet">
    /// <item>The library removes the returned view from its parent when the scan session ends,
    /// but does not dispose it — ownership and lifetime are yours.</item>
    /// <item>The view holds the passed <c>Activity</c> as its <c>Context</c> for as long as
    /// the view itself is alive — this is normal and unavoidable (every Android <c>View</c>
    /// does this), but it means caching the view for reuse keeps that specific <c>Activity</c>
    /// instance alive for as long as the cache does.</item>
    /// </list>
    /// </remarks>
    public Func<Activity, View>? CustomOverlayFactory { get; set; }
}
