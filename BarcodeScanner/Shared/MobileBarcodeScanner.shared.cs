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
    private static readonly ConcurrentDictionary<string, MobileBarcodeScanner> ActiveScanners = new();
    private static readonly ConcurrentDictionary<string, BarcodeScanningOptions> OptionsRegistry = new();
    
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
        ActiveScanners[InstanceId] = this;
        OptionsRegistry[InstanceId] = options;
    }
    
    internal static void DispatchSingleResult(string instanceId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchSingleResult вызван с пустым _instanceId.");
            return;
        }
        
        if (ActiveScanners.TryRemove(instanceId, out var scanner)) 
        {
            scanner.CompleteSingleScan(result);
        }
        Cleanup(instanceId);
    }
    
    internal static void DispatchContinuousResult(string instanceId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchContinuousResult вызван с пустым _instanceId.");
            return;
        }
        
        if (ActiveScanners.TryGetValue(instanceId, out var scanner)) 
        {
            scanner.TriggerContinuousCallback(result);
        }
    }

    internal static void DispatchCancel(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchCancel вызван с пустым _instanceId.");
            return;
        }
        
        if (ActiveScanners.TryRemove(instanceId, out var scanner))
        {
            scanner.CompleteContinuousScan();
            scanner.CancelAllScans(ScanStatus.CancelledByUser);
        }
        Cleanup(instanceId);
    }

    internal static void DispatchError(string instanceId, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchError вызван с пустым _instanceId.");
            return;
        }

        if (ActiveScanners.TryRemove(instanceId, out var scanner))
        {
            scanner.FailScan(errorMessage);
        }
        Cleanup(instanceId);
    }

    private static void Cleanup(string instanceId)
    {
        ActiveScanners.TryRemove(instanceId, out _);
        OptionsRegistry.TryRemove(instanceId, out _);
    }

    internal static BarcodeScanningOptions GetOptions(string instanceId) => 
        OptionsRegistry.TryGetValue(instanceId, out var options) ? options : new BarcodeScanningOptions();
    
    private partial Task<BarcodeResult?> PlatformScanSingleAsync();
    private partial Task PlatformScanContinuousAsync();
    private partial void PlatformCancelScan();
    private partial void PlatformSetTorch(bool torchOn);
}