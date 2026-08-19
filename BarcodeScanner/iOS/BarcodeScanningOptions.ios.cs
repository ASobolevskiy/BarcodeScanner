using UIKit;

namespace BarcodeScanner.Models;

public partial class BarcodeScanningOptions
{
    /// <summary>
    /// Factory for creating a custom scanner overlay. Called once per scan session, on the UI
    /// thread, during scanner setup. Must return a new <see cref="UIView"/> instance — the
    /// current <see cref="UIViewController"/> is passed as the argument so you don't need to
    /// capture it from elsewhere. Implement <see cref="IActiveScannerOverlay"/> on the returned
    /// view if you want it to receive live updates from the scan engine (detected barcode
    /// positions, region-of-interest sync, reset notifications) — otherwise it's shown but
    /// never updated.
    /// </summary>
    /// <remarks>
    /// ⚠️ WARNING: return a fresh instance on every call — never a cached/reused one.
    /// <list type="bullet">
    /// <item>The library removes the returned view from its parent when the scan session ends,
    /// but does not dispose it — ownership and lifetime are yours.</item>
    /// <item>Do not store the passed <c>UIViewController</c> anywhere that outlives this call
    /// (a field, a closure) — a saved reference is never garbage-collected, because the
    /// .NET-for-iOS runtime cannot collect a reference cycle that crosses a natively-retained
    /// object. This is not a slow leak — it never resolves.</item>
    /// </list>
    /// </remarks>
    public Func<UIViewController, UIView>? CustomOverlayFactory { get; set; }
}
