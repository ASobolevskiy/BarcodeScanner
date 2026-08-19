using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace BarcodeScanner;

/// <summary>
/// Cross-platform contract for scanning barcodes/QR codes via the device camera.
/// Call <see cref="ScanAsync"/> for a single scan or <see cref="ScanContinuouslyAsync"/>
/// for continuous scanning.
/// </summary>
public sealed partial class MobileBarcodeScanner : IMobileBarcodeScanner
{
    // Keyed by scan session, not by scanner instance - see _sessionId below for why that
    // distinction matters (CONC-01, fixed 2026-08-12).
    private static readonly ConcurrentDictionary<string, ScannerRegistration> Registrations = new ();
    private readonly BarcodeScanningOptions _defaultOptions = new();

    // Registry key of the scan session currently live on this instance, null when idle. One
    // key per *scan*, not per scanner instance (previously a single InstanceId was generated
    // once in the constructor and reused for every scan - see CONC-01 in CONTEXT.md): a native
    // screen still tearing down from a previous session can no longer resolve to the session
    // running now, because its id was single-use and is never re-added once removed.
    // Published last, after the registry entry (see ScanAsync/ScanContinuouslyAsync); cleared
    // before the guard is released by whichever thread wins ownership of the session (see
    // CancelCurrentSession/CompleteSingleScan/FailScan) - so a thread that just passed the
    // _isScanningState guard is always guaranteed to observe null here.
    private string? _sessionId;

    private TaskCompletionSource<BarcodeResult>? _singleScanTcs;
    private TaskCompletionSource? _continuousScanTcs;
    private Action<BarcodeResult>? _continuousCallback;

    // 0 = idle, 1 = a scan is in progress. Claimed atomically via CompareExchange so two concurrent
    // ScanAsync/ScanContinuouslyAsync calls on the same instance can't both pass the guard (TOCTOU).
    // Only ever released by the thread that owns the current session's registry entry - see _sessionId.
    private int _isScanningState;

    private CancellationTokenSource? _autoCloseCts;
    private CancellationTokenRegistration? _autoCloseRegistration;

    // 0 = off, 1 = on. Stored as int (not bool) so the toggle can be done atomically via
    // Interlocked.CompareExchange - a plain bool read-modify-write here loses updates under
    // concurrent ToggleTorch() calls (e.g. an external hardware trigger racing a custom overlay's
    // own torch button), leaving tracked state out of sync with the real hardware (CONC-07 in
    // CONTEXT.md).
    private int _isTorchOnState;

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
    public Task<BarcodeResult> ScanAsync(BarcodeScanningOptions? options = null)
    {
        if (Interlocked.CompareExchange(ref _isScanningState, 1, 0) != 0)
        {
            return Task.FromResult(new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = "Scanning is already in progress. Wait for completion or create new instance of scanner."
            });
        }

        var sessionId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<BarcodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _singleScanTcs, tcs);

        // Tracks whether the session became visible to other threads (registry + _sessionId
        // published) before something failed, so the catch block below knows whether it's safe
        // to clean up itself or whether it must defer to whoever else may already own the
        // session (see CONC-01/A3 in CONTEXT.md).
        var published = false;
        try
        {
            var finalOptions = (options ?? _defaultOptions).Clone();
            finalOptions.ScannerMode = ScanType.OneShot;
            finalOptions.SanitizePossibleFormats();

            if (finalOptions is { UseAutoClose: true, AutoCloseDelaySeconds: > 0 })
            {
                _autoCloseCts = new CancellationTokenSource(TimeSpan.FromSeconds(finalOptions.AutoCloseDelaySeconds));
                _autoCloseRegistration = _autoCloseCts.Token.Register(CancelByAutoClose);
            }

            // Publish last: from this point the session is visible and a terminator may claim it.
            Registrations[sessionId] = new ScannerRegistration(this, null, finalOptions);
            Volatile.Write(ref _sessionId, sessionId);
            published = true;

            PlatformStartScanner(sessionId);
        }
        catch (Exception ex)
        {
            var errorResult = new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = $"Failed to start platform scanner: {ex.Message}"
            };

            if (!published)
            {
                // Nobody else can see this session yet, so nobody else can be racing to
                // terminate it - safe to clean up directly.
                CleanupAutoClose();
                Interlocked.Exchange(ref _singleScanTcs, null);
                Volatile.Write(ref _sessionId, null);
                Interlocked.Exchange(ref _isScanningState, 0);
                tcs.TrySetResult(errorResult);
            }
            else if (Registrations.TryRemove(sessionId, out var registration))
            {
                FailScan(errorResult.ErrorMessage!, registration.PlatformSession);
            }
            // else: another thread already claimed and terminated this session and already
            // completed `tcs` with the real outcome - nothing to do here.
        }

        return tcs.Task;
    }

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
    public Task ScanContinuouslyAsync(
        Action<BarcodeResult> onResult,
        BarcodeScanningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(onResult);

        if (Interlocked.CompareExchange(ref _isScanningState, 1, 0) != 0)
        {
            onResult(new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = "Scanning is already in progress. Wait for completion or create new instance of scanner."
            });
            return Task.CompletedTask;
        }

        var sessionId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _continuousScanTcs, tcs);
        Volatile.Write(ref _continuousCallback, onResult);

        var published = false;
        try
        {
            var finalOptions = (options ?? _defaultOptions).Clone();
            finalOptions.ScannerMode = ScanType.Continuous;
            finalOptions.SanitizePossibleFormats();

            if (finalOptions.UseAutoClose)
                Debug.WriteLine("[BarcodeScanner] Warning: UseAutoClose has no effect in continuous scan mode.");

            Registrations[sessionId] = new ScannerRegistration(this, null, finalOptions);
            Volatile.Write(ref _sessionId, sessionId);
            published = true;

            PlatformStartScanner(sessionId);
        }
        catch (Exception ex)
        {
            var errorResult = new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = $"Failed to start platform scanner: {ex.Message}"
            };

            if (!published)
            {
                Interlocked.Exchange(ref _continuousScanTcs, null);
                var callback = Interlocked.Exchange(ref _continuousCallback, null);
                Volatile.Write(ref _sessionId, null);
                Interlocked.Exchange(ref _isScanningState, 0);
                callback?.Invoke(errorResult);
                tcs.TrySetResult();
            }
            else if (Registrations.TryRemove(sessionId, out var registration))
            {
                FailScan(errorResult.ErrorMessage!, registration.PlatformSession);
            }
        }

        return tcs.Task;
    }

    /// <summary>
    /// Cancels the in-progress scan started by <see cref="ScanAsync"/> or
    /// <see cref="ScanContinuouslyAsync"/> and dismisses the scanner screen.
    /// </summary>
    /// <remarks>Does nothing if no scan is currently in progress on this instance.</remarks>
    public void CancelScan() => CancelCurrentSession(ScanStatus.CancelledByUser);

    private void CancelByAutoClose() => CancelCurrentSession(ScanStatus.AutoClosed);

    private void CancelCurrentSession(ScanStatus reason)
    {
        // Claiming the session (winning TryRemove) is what makes this thread its sole
        // terminator. If there's no live session, or another thread already claimed it (a
        // result/error arrived, or another cancel got there first), this is a true no-op - in
        // particular it must NOT release _isScanningState for a session it doesn't own
        // (CONC-01 in CONTEXT.md).
        var sessionId = Volatile.Read(ref _sessionId);
        if (sessionId is null || !Registrations.TryRemove(sessionId, out var registration))
            return;

        // Ask the native screen to close before tearing down this session's state (CONC-01/A1
        // in CONTEXT.md): releasing the guard first would let a new scan start and show its own
        // screen while the old one is still up, and closing the old one could take the new one
        // down with it.
        registration.PlatformSession?.RequestCancel();
        CompleteCancelledSession(reason);
    }

    // Caller must already own the session (won removal of its registry entry).
    private void CompleteCancelledSession(ScanStatus reason)
    {
        Volatile.Write(ref _sessionId, null);
        CleanupAutoClose();

        var singleTcs = Interlocked.Exchange(ref _singleScanTcs, null);
        var contTcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        Interlocked.Exchange(ref _continuousCallback, null);
        Interlocked.Exchange(ref _isScanningState, 0);

        if (singleTcs is null && contTcs is null)
            return;

        PlatformPostToMain(() =>
        {
            singleTcs?.TrySetResult(new BarcodeResult { Status = reason });
            contTcs?.TrySetResult();
        });
    }

    // Caller must already own the session (won removal of its registry entry).
    private void FailScan(string errorMessage, IPlatformScannerSession? platformSession)
    {
        // Ask the native screen to close first, before this session's state is torn down -
        // mirrors CancelCurrentSession's ordering (CONC-01/A1 in CONTEXT.md).
        platformSession?.RequestCancel();

        Volatile.Write(ref _sessionId, null);
        CleanupAutoClose();
        var errorResult = new BarcodeResult { Status = ScanStatus.Error, ErrorMessage = errorMessage };

        var singleTcs = Interlocked.Exchange(ref _singleScanTcs, null);
        var contTcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        var callback = Interlocked.Exchange(ref _continuousCallback, null);
        Interlocked.Exchange(ref _isScanningState, 0);

        if (singleTcs is not null || callback is not null || contTcs is not null)
        {
            PlatformPostToMain(() =>
            {
                singleTcs?.TrySetResult(errorResult);
                callback?.Invoke(errorResult);
                contTcs?.TrySetResult();
            });
        }
    }

    /// <summary>
    /// Toggles the camera torch (flashlight) on or off.
    /// </summary>
    /// <remarks>Has no effect outside an active scan session.</remarks>
    public void ToggleTorch()
    {
        int oldState, newState;
        do
        {
            oldState = Volatile.Read(ref _isTorchOnState);
            newState = oldState == 0 ? 1 : 0;
        } while (Interlocked.CompareExchange(ref _isTorchOnState, newState, oldState) != oldState);

        var isTorchOn = newState != 0;
        var sessionId = Volatile.Read(ref _sessionId);
        if (sessionId is not null && Registrations.TryGetValue(sessionId, out var reg) && reg.PlatformSession != null)
        {
            reg.PlatformSession.SetTorch(isTorchOn);
        }
    }

    private void TriggerContinuousCallback(string sessionId, BarcodeResult result)
    {
        // Gate on the session id (not just _isScanningState) so a result already "in flight"
        // when this session ends - or gets replaced by a new one - is dropped instead of
        // reaching the wrong consumer late. Checked once here as a cheap early exit, and again
        // inside the hop at the moment of actual delivery, since PlatformPostToMain defers
        // execution and the session can change meanwhile. The comparison is explicitly ordinal
        // on purpose (CONC-01/A4 in CONTEXT.md): two equal-value strings are not always the
        // same instance (Android's sessionId round-trips through Intent.PutExtra/GetStringExtra),
        // so a future refactor toward reference-based comparison would silently break this.
        if (!string.Equals(Volatile.Read(ref _sessionId), sessionId, StringComparison.Ordinal))
            return;

        var callback = Volatile.Read(ref _continuousCallback);
        if (callback is null)
            return;

        PlatformPostToMain(() =>
        {
            if (!string.Equals(Volatile.Read(ref _sessionId), sessionId, StringComparison.Ordinal))
                return;
            callback.Invoke(result);
        });
    }

    private void CompleteSingleScan(BarcodeResult result)
    {
        Volatile.Write(ref _sessionId, null);
        CleanupAutoClose();
        result = result with { Status = ScanStatus.Success };
        var tcs = Interlocked.Exchange(ref _singleScanTcs, null);
        Interlocked.Exchange(ref _isScanningState, 0);

        if (tcs is null)
            return;

        PlatformPostToMain(() => tcs.TrySetResult(result));
    }

    private void CleanupAutoClose()
    {
        _autoCloseRegistration?.Dispose();
        _autoCloseRegistration = null;
        _autoCloseCts?.Dispose();
        _autoCloseCts = null;
    }

    internal static void AttachPlatformSession(string sessionId, IPlatformScannerSession session)
    {
        while (Registrations.TryGetValue(sessionId, out var oldRegistration))
        {
            var newRegistration = oldRegistration with { PlatformSession = session };
            if (Registrations.TryUpdate(sessionId, newRegistration, oldRegistration))
                return;
        }
        // No live session for this id - it was already terminated (e.g. a cancel raced the
        // native screen's own setup). The screen is orphaned but harmless: it holds an id
        // nothing can look up anymore, so it can no longer interfere with whatever scan (if any)
        // is running now.
        Debug.WriteLine($"[BarcodeScanner] AttachPlatformSession: no registration found for session {sessionId} (already terminated).");
    }

    internal static void DispatchSingleResult(string sessionId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchSingleResult called with empty sessionId.");
            return;
        }

        if (!Registrations.TryRemove(sessionId, out var registration))
            return;

        registration.Scanner.CompleteSingleScan(result);
    }

    internal static void DispatchContinuousResult(string sessionId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchContinuousResult called with empty sessionId.");
            return;
        }

        if (!Registrations.TryGetValue(sessionId, out var registration))
            return;

        registration.Scanner.TriggerContinuousCallback(sessionId, result);
    }

    internal static void DispatchCancel(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchCancel called with empty sessionId.");
            return;
        }

        var removed = Registrations.TryRemove(sessionId, out var registration);
        Debug.WriteLine($"[BarcodeScanner] DispatchCancel: SessionId={sessionId}, Removed={removed}, Registrations={Registrations.Count}");
        if (!removed)
            return;

        registration.Scanner.CompleteCancelledSession(ScanStatus.CancelledByUser);
    }

    internal static void DispatchError(string sessionId, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchError called with empty sessionId.");
            return;
        }

        if (!Registrations.TryRemove(sessionId, out var registration))
            return;

        registration.Scanner.FailScan(errorMessage, registration.PlatformSession);
    }

    internal static BarcodeScanningOptions GetOptions(string sessionId) =>
        Registrations.TryGetValue(sessionId, out var registration)
            ? registration.Options
            : new BarcodeScanningOptions();

    private partial void PlatformStartScanner(string sessionId);
    private partial void PlatformPostToMain(Action action);
}
