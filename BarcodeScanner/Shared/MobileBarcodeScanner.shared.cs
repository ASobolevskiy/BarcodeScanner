using BarcodeScanner.Models;
using BarcodeScanner.Shared.Enums;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner : IMobileBarcodeScanner
{
    private readonly BarcodeScanningOptions _defaultOptions = new();
    internal string InstanceId { get; } = Guid.NewGuid().ToString();
    
    private TaskCompletionSource<BarcodeResult?>? _singleScanTcs;
    private TaskCompletionSource<bool>? _continuousScanTcs;
    private Action<BarcodeResult?>? _continuousCallback;
    private CancellationTokenSource? _autoCloseCts;
    private IDisposable? _autoCloseRegistration;
    
    private bool _isTorchOn;
    private CancellationTokenSource _cts;

    public Task<BarcodeResult?> ScanAsync(BarcodeScanningOptions? options = null)
    {
        EnsureNotScanning();
        _singleScanTcs = new TaskCompletionSource<BarcodeResult?>();
        
        var finalOptions = options ?? _defaultOptions;
        finalOptions.ScannerMode = BarcodeScanningOptions.ScanType.OneShot;

        if (finalOptions is { UseAutoClose: true, AutoCloseDelaySeconds: > 0 })
        {
            _autoCloseCts = new CancellationTokenSource(TimeSpan.FromSeconds(finalOptions.AutoCloseDelaySeconds));
            _autoCloseRegistration = _autoCloseCts.Token.Register(CancelByAutoClose);
        }
        return PlatformScanSingleAsync(finalOptions);
    }

    public Task ScanContinuouslyAsync(
        BarcodeScanningOptions? options, 
        Action<BarcodeResult?> onResult)
    {
        EnsureNotScanning();
        _cts = new CancellationTokenSource();
        _continuousScanTcs = new TaskCompletionSource<bool>();
        
        var finalOptions = options ?? _defaultOptions;
        finalOptions.ScannerMode = BarcodeScanningOptions.ScanType.Continuous;
        _cts.Token.Register(CancelScan);
        _continuousCallback = onResult;
        
        return PlatformScanContinuousAsync(finalOptions);
    }

    public void CancelScan()
    {
        CleanupAutoClose();
        CancelAllScans(ScanStatus.CancelledByUser);
        PlatformCancelScan();
    }
    
    internal void CancelByAutoClose()
    {
        CleanupAutoClose();
        CancelAllScans(ScanStatus.AutoClosed);
        PlatformCancelScan();
    }

    internal void FailScan(string errorMessage)
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
    
    internal void CompleteSingleScan(BarcodeResult result)
    {
        CleanupAutoClose();
        result = result with { Status = ScanStatus.Success };
        var tcs = Interlocked.Exchange(ref _singleScanTcs, null);
        tcs?.TrySetResult(result);
    }
    
    internal void CompleteContinuousScan()
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
    
    private partial Task<BarcodeResult?> PlatformScanSingleAsync(BarcodeScanningOptions options);
    private partial Task PlatformScanContinuousAsync(BarcodeScanningOptions options);
    private partial void PlatformCancelScan();
    private partial void PlatformSetTorch(bool torchOn);
}