using System.Diagnostics;
using BarcodeScanner.Helpers;
using Activity = Android.App.Activity;

namespace BarcodeScanner;

/// <summary>
/// Android-only setup for scanning. Call <see cref="Init(Application)"/> or
/// <see cref="Init(Activity)"/> once, as early as possible (e.g. in <c>OnCreate</c> of your
/// <c>Application</c> or main <c>Activity</c>), so the library can track the current
/// foreground activity needed to present the scanner screen.
/// </summary>
/// <remarks>
/// If neither overload is called explicitly, the library attempts to self-register on first
/// use — this is not guaranteed to succeed in every hosting scenario, so calling
/// <see cref="Init(Application)"/> or <see cref="Init(Activity)"/> explicitly is recommended.
/// If both the explicit call and the automatic fallback are skipped or fail, scanning throws
/// <see cref="InvalidOperationException"/> when started.
/// </remarks>
public static class MobileBarcodeScannerPlatform
{
    private static volatile bool isInitialized;
    private static readonly object LockObject = new();

    /// <summary>
    /// Registers activity lifecycle tracking using the application instance. Safe to call
    /// more than once — only the first call has any effect.
    /// </summary>
    /// <param name="app">Your application instance.</param>
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

    /// <summary>
    /// Registers activity lifecycle tracking using an activity's application instance. Safe
    /// to call more than once — only the first call has any effect.
    /// </summary>
    /// <param name="activity">Any activity in your app — typically your main activity.</param>
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