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
    private TaskCompletionSource<bool>? _continuousScanTcs;
    private Action<BarcodeResult?>? _continuousCallback;
    private CancellationTokenSource? _autoCloseCts;
    private CancellationTokenRegistration? _autoCloseRegistration;
    
    private bool _isTorchOn;
    private CancellationTokenSource? _cts;

    public Task<BarcodeResult?> ScanAsync(BarcodeScanningOptions? options = null)
    {
        CleanupOrphanedRegistrations();
        EnsureNotScanning();
        _singleScanTcs = new TaskCompletionSource<BarcodeResult?>();
        
        var finalOptions = options ?? _defaultOptions;
        finalOptions.ScannerMode = ScanType.OneShot;
        
        RegisterInstance(finalOptions);

        if (finalOptions is { UseAutoClose: true, AutoCloseDelaySeconds: > 0 })
        {
            _autoCloseCts = new CancellationTokenSource(TimeSpan.FromSeconds(finalOptions.AutoCloseDelaySeconds));
            _autoCloseRegistration = _autoCloseCts.Token.Register(CancelByAutoClose);
        }
        return PlatformScanSingleAsync();
    }

    public Task ScanContinuouslyAsync(
        BarcodeScanningOptions? options, 
        Action<BarcodeResult?> onResult)
    {
        CleanupOrphanedRegistrations();
        EnsureNotScanning();
        _cts = new CancellationTokenSource();
        _continuousScanTcs = new TaskCompletionSource<bool>();
        
        var finalOptions = options ?? _defaultOptions;
        finalOptions.ScannerMode = ScanType.Continuous;
        
        RegisterInstance(finalOptions);
        
        _cts.Token.Register(CancelScan);
        _continuousCallback = onResult;
        
        return PlatformScanContinuousAsync();
    }

    public void CancelScan()
    {
        CleanupAutoClose();
        CancelAllScans(ScanStatus.CancelledByUser);
        PlatformCancelScan();
    }

    private void CancelByAutoClose()
    {
        CleanupAutoClose();
        CancelAllScans(ScanStatus.AutoClosed);
        PlatformCancelScan();
    }

    private void FailScan(string errorMessage)
    {
        CleanupAutoClose();
        var singleTcs = Interlocked.Exchange(ref _singleScanTcs, null);
        singleTcs?.TrySetResult(new BarcodeResult { Status = ScanStatus.Error, ErrorMessage = errorMessage });
        
        var contTcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        contTcs?.TrySetResult(true);
        _continuousCallback = null;
        
        PlatformCancelScan();
    }

    public void ToggleTorch()
    {
        _isTorchOn = !_isTorchOn;
        PlatformSetTorch(_isTorchOn);
    }

    private void EnsureNotScanning()
    {
        if (_singleScanTcs != null || _continuousScanTcs != null)
        {
            throw new InvalidOperationException("Scanning is already in progress. Wait for completion or create new instance of scanner.");
        }
    }

    private void TriggerContinuousCallback(BarcodeResult result)
    {
        _continuousCallback?.Invoke(result);
    }

    private void CompleteSingleScan(BarcodeResult result)
    {
        CleanupAutoClose();
        result = result with { Status = ScanStatus.Success };
        var tcs = Interlocked.Exchange(ref _singleScanTcs, null);
        tcs?.TrySetResult(result);
    }

    private void CompleteContinuousScan()
    {
        CleanupAutoClose();
        var tcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        tcs?.TrySetResult(true);
        _continuousCallback = null;
    }
    
    private void CancelAllScans(ScanStatus reason)
    {
        CleanupAutoClose();
        var singleTcs = Interlocked.Exchange(ref _singleScanTcs, null);
        singleTcs?.TrySetResult(new BarcodeResult { Status = reason });

        var contTcs = Interlocked.Exchange(ref _continuousScanTcs, null);
        contTcs?.TrySetResult(true);
        
        _continuousCallback = null;
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
        Registrations[InstanceId] = new ScannerRegistration(new WeakReference<MobileBarcodeScanner>(this),
                                                            _singleScanTcs,
                                                            _continuousScanTcs,
                                                            options);
    }
    
    internal static void DispatchSingleResult(string instanceId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchSingleResult called with empty instanceId.");
            return;
        }

        if (!Registrations.TryGetValue(instanceId, out var registration))
            return;
        
        if (registration.Scanner.TryGetTarget(out var scanner))
        {
            scanner.CompleteSingleScan(result);
        }
        else
        {
            registration.SingleScanTcs?.TrySetCanceled();
            registration.ContinuousScanTcs?.TrySetCanceled();
        }
            
        Registrations.TryRemove(instanceId, out _);
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

        if (registration.Scanner.TryGetTarget(out var scanner))
        {
            scanner.TriggerContinuousCallback(result);
        }
        else
        {
            registration.SingleScanTcs?.TrySetCanceled();
            registration.ContinuousScanTcs?.TrySetCanceled();
            Registrations.TryRemove(instanceId, out _);
        }
    }

    internal static void DispatchCancel(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchCancel called with empty instanceId.");
            return;
        }

        if (!Registrations.TryGetValue(instanceId, out var registration)) 
            return;
        
        if (registration.Scanner.TryGetTarget(out var scanner))
        {
            scanner.CompleteContinuousScan();
            scanner.CancelAllScans(ScanStatus.CancelledByUser);
        }
        else
        {
            registration.SingleScanTcs?.TrySetCanceled();
            registration.ContinuousScanTcs?.TrySetCanceled();
        }
        
        Registrations.TryRemove(instanceId, out _);
    }

    internal static void DispatchError(string instanceId, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] Error: DispatchError called with empty instanceId.");
            return;
        }

        if (!Registrations.TryGetValue(instanceId, out var registration)) 
            return;
        
        if (registration.Scanner.TryGetTarget(out var scanner))
        {
            scanner.FailScan(errorMessage);
        }
        else
        {
            registration.SingleScanTcs?.TrySetCanceled();
            registration.ContinuousScanTcs?.TrySetCanceled();
        }
        
        Registrations.TryRemove(instanceId, out _);
    }

    internal static BarcodeScanningOptions GetOptions(string instanceId) =>
        Registrations.TryGetValue(instanceId, out var registration)
            ? registration.Options
            : new BarcodeScanningOptions();

    private static void CleanupOrphanedRegistrations()
    {
        var count = Registrations.Count;
        if (count == 0) return;
    
        var orphanedKeys = new List<string>(count);
    
        foreach (var kvp in Registrations)
        {
            if (!kvp.Value.Scanner.TryGetTarget(out _))
            {
                orphanedKeys.Add(kvp.Key);
            }
        }
    
        foreach (var key in orphanedKeys)
        {
            if (!Registrations.TryRemove(key, out var registration)) 
                continue;
            
            registration.SingleScanTcs?.TrySetCanceled();
            registration.ContinuousScanTcs?.TrySetCanceled();
        }
    }
    
    private partial Task<BarcodeResult?> PlatformScanSingleAsync();
    private partial Task PlatformScanContinuousAsync();
    private partial void PlatformCancelScan();
    private partial void PlatformSetTorch(bool torchOn);
}