using System.Diagnostics;
using BarcodeScanner.Helpers;
using Activity = Android.App.Activity;

namespace BarcodeScanner;

public static class MobileBarcodeScannerPlatform
{
    private static volatile bool isInitialized;
    private static readonly object LockObject = new();

    public static void Init(Application app)
    {
        if (isInitialized) return;
        lock (LockObject)
        {
            if(isInitialized) return;
            
            app.RegisterActivityLifecycleCallbacks(new ActivityLifecycleTracker());
            isInitialized = true;
        }
    }
    
    public static void Init(Activity activity)
    {
        if (isInitialized) return;
        lock (LockObject)
        {
            if(isInitialized) return;
            
            activity.Application?.RegisterActivityLifecycleCallbacks(new ActivityLifecycleTracker());
            isInitialized = true;
        }
    }

    internal static Activity? GetCurrentActivity()
    {
        if (isInitialized)
        {
            return ActivityLifecycleTracker.CurrentActivity;
        }

        lock (LockObject)
        {
            if (!isInitialized)
            {
                try
                {
                    if (Application.Context is Application app)
                    {
                        app.RegisterActivityLifecycleCallbacks(new ActivityLifecycleTracker());
                        isInitialized = true;
                    }
                }
                catch(Exception ex)
                {
                    Debug.WriteLine($"[BarcodeScanner] Failed to automatically register ActivityTracker: {ex.Message}");
                }
            }
        }
        
        return ActivityLifecycleTracker.CurrentActivity ?? throw new InvalidOperationException(
             "[BarcodeScanner] Cannot get instance of current top Activity." +
             "You should explicitly call MobileBarcodeScannerPlatform.Init(this) in the OnCreate() method" +
             "of your MainActivity or custom Application class");
    }
}