using Android.Content;
using Android.OS;
using BarcodeScanner.Models;
using Activity = Android.App.Activity;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner
{
    // Process-wide and never disposed - this is not tied to any Activity's lifecycle, so it
    // must never end up in an Activity's teardown/SafeCleanup disposal list.
    private static readonly Handler MainHandler = new(Looper.MainLooper!);

    private partial void PlatformPostToMain(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return;
        }
        MainHandler.Post(action);
    }

    private partial void PlatformStartScanner(string sessionId)
    {
        var activity = GetCurrentActivity();
        var intent = new Intent(activity, typeof(BarcodeScannerActivity));
        intent.PutExtra("scanner_instance_id", sessionId);
        activity.StartActivity(intent);
    }

    private Activity GetCurrentActivity() =>
        MobileBarcodeScannerPlatform.GetCurrentActivity() 
        ?? throw new InvalidOperationException("Failed to get current Activity.");
}