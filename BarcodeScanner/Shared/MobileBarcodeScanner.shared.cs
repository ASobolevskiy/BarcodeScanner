using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace BarcodeScanner;

/// <inheritdoc/>
public partial class MobileBarcodeScanner : IMobileBarcodeScanner
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

    private bool _isTorchOn;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void ToggleTorch()
    {
        _isTorchOn = !_isTorchOn;

        var sessionId = Volatile.Read(ref _sessionId);
        if (sessionId is not null && Registrations.TryGetValue(sessionId, out var reg) && reg.PlatformSession != null)
        {
            reg.PlatformSession.SetTorch(_isTorchOn);
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
