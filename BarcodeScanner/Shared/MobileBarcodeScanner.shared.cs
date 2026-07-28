using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner : IMobileBarcodeScanner
{
    private static readonly ConcurrentDictionary<string, ScannerRegistration> Registrations = new ();
    private readonly BarcodeScanningOptions _defaultOptions = new();
    
    private string InstanceId { get; } = Guid.NewGuid().ToString();
    
    private TaskCompletionSource<BarcodeResult?>? _singleScanTcs;
    private TaskCompletionSource? _continuousScanTcs;
    private Action<BarcodeResult?>? _continuousCallback;

    // 0 = idle, 1 = a scan is in progress. Claimed atomically via CompareExchange so two concurrent
    // ScanAsync/ScanContinuouslyAsync calls on the same instance can't both pass the guard (TOCTOU).
    private int _isScanningState;
    
    private CancellationTokenSource? _autoCloseCts;
    private CancellationTokenRegistration? _autoCloseRegistration;
    
    private bool _isTorchOn;

    public Task<BarcodeResult?> ScanAsync(BarcodeScanningOptions? options = null)
    {
        if (Interlocked.CompareExchange(ref _isScanningState, 1, 0) != 0)
        {
            return Task.FromResult<BarcodeResult?>(new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = "Scanning is already in progress. Wait for completion or create new instance of scanner."
            });
        }

        _singleScanTcs = new TaskCompletionSource<BarcodeResult?>();

        var finalOptions = (options ?? _defaultOptions).Clone();
        finalOptions.ScannerMode = ScanType.OneShot;
        RegisterInstance(finalOptions);

        if (finalOptions is { UseAutoClose: true, AutoCloseDelaySeconds: > 0 })
        {
            _autoCloseCts = new CancellationTokenSource(TimeSpan.FromSeconds(finalOptions.AutoCloseDelaySeconds));
            _autoCloseRegistration = _autoCloseCts.Token.Register(CancelByAutoClose);
        }

        try
        {
            return PlatformScanSingleAsync();
        }
        catch (Exception ex)
        {
            var tcs = Interlocked.Exchange(ref _singleScanTcs, null);

            var errorResult = new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = $"Failed to start platform scanner: {ex.Message}"
            };

            tcs?.TrySetResult(errorResult);

            Registrations.TryRemove(InstanceId, out _);
            CleanupAutoClose();
            Interlocked.Exchange(ref _isScanningState, 0);

            return tcs?.Task ?? Task.FromResult<BarcodeResult?>(errorResult);
        }
    }

    public Task ScanContinuouslyAsync(
        BarcodeScanningOptions? options,
        Action<BarcodeResult?> onResult)
    {
        if (Interlocked.CompareExchange(ref _isScanningState, 1, 0) != 0)
        {
            onResult(new BarcodeResult
            {
                Status = ScanStatus.Error,
                ErrorMessage = "Scanning is already in progress. Wait for completion or create new instance of scanner."
            });
            return Task.CompletedTask;
        }

        _continuousScanTcs = new TaskCompletionSource();

        var finalOptions = (options ?? _defaultOptions).Clone();
        finalOptions.ScannerMode = ScanType.Continuous;
        RegisterInstance(finalOptions);
        
        Volatile.Write(ref _continuousCallback, onResult);

        try
        {
            return PlatformScanContinuousAsync();
        }
        catch (Exception ex)
        {
            var tcs = Interlocked.Exchange(ref _continuousScanTcs, null);
            
            var errorResult = new BarcodeResult 
            { 
                Status = ScanStatus.Error, 
                ErrorMessage = $"Failed to start platform scanner: {ex.Message}" 
            };

            var callback = _continuousCallback;
            callback?.Invoke(errorResult);

            tcs?.TrySetResult();

            Registrations.TryRemove(InstanceId, out _);
            CleanupAutoClose();
            Interlocked.Exchange(ref _isScanningState, 0);

            return tcs?.Task ?? Task.CompletedTask;
        }
    }

    public void CancelScan()
    {
        CleanupAutoClose();
        CancelAllScans(ScanStatus.CancelledByUser);

        if (Registrations.TryGetValue(InstanceId, out var reg) && reg.PlatformSession is not null)
        {
            reg.PlatformSession.RequestCancel();
        }
    }

    private void CancelByAutoClose()
    {
        CleanupAutoClose();
        CancelAllScans(ScanStatus.AutoClosed);
        
        if (Registrations.TryGetValue(InstanceId, out var reg) && reg.PlatformSession is not null)
        {
            reg.PlatformSession.RequestCancel();
        }
    }

    private void FailScan(string errorMessage)
    {
        CleanupAutoClose();
        var errorResult = new BarcodeResult { Status = ScanStatus.Error, ErrorMessage = errorMessage };

        var singleTcs = Interlocked.Exchange(ref _singleScanTcs, null);
        singleTcs?.TrySetResult(errorResult);

        var contTcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        var callback = Interlocked.Exchange(ref _continuousCallback, null);
        callback?.Invoke(errorResult);
        contTcs?.TrySetResult();
        Interlocked.Exchange(ref _isScanningState, 0);

        if (Registrations.TryGetValue(InstanceId, out var reg) && reg.PlatformSession is not null)
        {
            reg.PlatformSession.RequestCancel();
        }
    }

    public void ToggleTorch()
    {
        _isTorchOn = !_isTorchOn;
        
        if (Registrations.TryGetValue(InstanceId, out var reg) && reg.PlatformSession != null)
        {
            reg.PlatformSession.SetTorch(_isTorchOn);
        }
    }

    private void TriggerContinuousCallback(BarcodeResult result)
    {
        // Gate on scanning state so a result already "in flight" when CancelScan()/FailScan
        // completes on another thread is dropped instead of reaching the consumer late.
        if (Volatile.Read(ref _isScanningState) != 1)
            return;

        var callback = Volatile.Read(ref _continuousCallback);
        callback?.Invoke(result);
    }

    private void CompleteSingleScan(BarcodeResult result)
    {
        CleanupAutoClose();
        result = result with { Status = ScanStatus.Success };
        var tcs = Interlocked.Exchange(ref _singleScanTcs, null);
        tcs?.TrySetResult(result);
        Interlocked.Exchange(ref _isScanningState, 0);
    }

    private void CompleteContinuousScan()
    {
        CleanupAutoClose();
        var tcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        tcs?.TrySetResult();
        Volatile.Write(ref _continuousCallback, null);
        Interlocked.Exchange(ref _isScanningState, 0);
    }

    private void CancelAllScans(ScanStatus reason)
    {
        CleanupAutoClose();

        var singleTcs = Interlocked.Exchange(ref _singleScanTcs, null);
        if (singleTcs is not null)
        {
            var cancelResult = new BarcodeResult { Status = reason };
            singleTcs.TrySetResult(cancelResult);
        }

        var contTcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        contTcs?.TrySetResult();

        Volatile.Write(ref _continuousCallback, null);
        Interlocked.Exchange(ref _isScanningState, 0);
    }

    private void CleanupAutoClose()
    {
        _autoCloseRegistration?.Dispose();
        _autoCloseRegistration = null;
        _autoCloseCts?.Dispose();
        _autoCloseCts = null;
    }
    
    private void RegisterInstance(BarcodeScanningOptions options)
    {
        Registrations[InstanceId] = new ScannerRegistration(this, null, options);
    }

    internal static void AttachPlatformSession(string instanceId, IPlatformScannerSession session)
    {
        while (Registrations.TryGetValue(instanceId, out var oldRegistration))
        {
            var newRegistration = oldRegistration with { PlatformSession = session };
            if (Registrations.TryUpdate(instanceId, newRegistration, oldRegistration))
                return;
        }
    }
    
    internal static void DispatchSingleResult(string instanceId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchSingleResult called with empty instanceId.");
            return;
        }

        if (!Registrations.TryRemove(instanceId, out var registration))
            return;
        
        registration.Scanner.CompleteSingleScan(result);
    }
    
    internal static void DispatchContinuousResult(string instanceId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchContinuousResult called with empty instanceId.");
            return;
        }

        if (!Registrations.TryGetValue(instanceId, out var registration))
            return;
        
        registration.Scanner.TriggerContinuousCallback(result);
    }

    internal static void DispatchCancel(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchCancel called with empty instanceId.");
            return;
        }

        var removed = Registrations.TryRemove(instanceId, out var registration);
        Debug.WriteLine($"[BarcodeScanner] DispatchCancel: InstanceId={instanceId}, Removed={removed}, Registrations={Registrations.Count}");
        if (!removed) 
            return;
        
        registration.Scanner.CompleteContinuousScan();
        registration.Scanner.CancelAllScans(ScanStatus.CancelledByUser);
    }

    internal static void DispatchError(string instanceId, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchError called with empty instanceId.");
            return;
        }

        if (!Registrations.TryRemove(instanceId, out var registration)) 
            return;
        
        registration.Scanner.FailScan(errorMessage);
    }

    internal static BarcodeScanningOptions GetOptions(string instanceId) =>
        Registrations.TryGetValue(instanceId, out var registration)
            ? registration.Options
            : new BarcodeScanningOptions();
    
    private partial Task<BarcodeResult?> PlatformScanSingleAsync();
    private partial Task PlatformScanContinuousAsync();
}