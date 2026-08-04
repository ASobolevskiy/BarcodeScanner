using Android.Content;
using BarcodeScanner.Models;
using Activity = Android.App.Activity;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner
{
    private partial Task<BarcodeResult> PlatformScanSingleAsync()
    {
        var intent = CreateIntent();
        GetCurrentActivity().StartActivity(intent);
        return _singleScanTcs!.Task;
    }

    private partial Task PlatformScanContinuousAsync()
    {
        var intent = CreateIntent();
        GetCurrentActivity().StartActivity(intent);
        return _continuousScanTcs!.Task;
    }
    
    private Intent CreateIntent()
    {
        var intent = new Intent(GetCurrentActivity(), typeof(BarcodeScannerActivity));
        intent.PutExtra("scanner_instance_id", InstanceId);
        return intent;
    }
    
    private Activity GetCurrentActivity() => 
        MobileBarcodeScannerPlatform.GetCurrentActivity() 
        ?? throw new InvalidOperationException("Failed to get current Activity.");
}