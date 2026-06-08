using System.Collections.Concurrent;
using System.Diagnostics;
using Android.Content;
using BarcodeScanner.Models;
using BarcodeScanner.Shared.Enums;
using Activity = Android.App.Activity;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner
{
    private static readonly ConcurrentDictionary<string, MobileBarcodeScanner> ActiveScanners = new();
    private static readonly ConcurrentDictionary<string, BarcodeScanningOptions> OptionsRegistry = new();
    
    private partial Task<BarcodeResult?> PlatformScanSingleAsync(BarcodeScanningOptions options)
    {
        RegisterInstance(options);
        var intent = CreateIntent();
        GetCurrentActivity().StartActivity(intent);
        return _singleScanTcs!.Task;
    }

    private partial Task PlatformScanContinuousAsync(BarcodeScanningOptions options)
    {
        RegisterInstance(options);
        var intent = CreateIntent();
        GetCurrentActivity().StartActivity(intent);
        return _continuousScanTcs!.Task;
    }

    private partial void PlatformCancelScan()
    {
        BarcodeScannerActivity.FinishByInstanceId(InstanceId);
    }

    private partial void PlatformSetTorch(bool torchOn)
    {
        BarcodeScannerActivity.SetTorchState(InstanceId, torchOn);
    }
    
    private void RegisterInstance(BarcodeScanningOptions options)
    {
        ActiveScanners[InstanceId] = this;
        OptionsRegistry[InstanceId] = options;
    }
    
    private Intent CreateIntent()
    {
        var intent = new Intent(GetCurrentActivity(), typeof(BarcodeScannerActivity));
        intent.PutExtra("scanner_instance_id", InstanceId);
        return intent;
    }
    
    private Activity GetCurrentActivity() => 
        MobileBarcodeScannerPlatform.GetCurrentActivity() 
        ?? throw new InvalidOperationException("Не удалось получить текущую Activity.");
    
    internal static void DispatchSingleResult(string instanceId, BarcodeResult result)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchSingleResult вызван с пустым InstanceId.");
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
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchContinuousResult вызван с пустым InstanceId.");
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
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchCancel вызван с пустым InstanceId.");
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
            Debug.WriteLine("[BarcodeScanner] ОШИБКА: DispatchError вызван с пустым InstanceId.");
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
}