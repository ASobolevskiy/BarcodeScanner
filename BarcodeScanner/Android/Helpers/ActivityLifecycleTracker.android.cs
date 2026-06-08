namespace BarcodeScanner.Helpers;

internal class ActivityLifecycleTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private static WeakReference<Activity>? currentActivity;

    public static Activity? CurrentActivity
    {
        get
        {
            Activity? activity = null;
            currentActivity?.TryGetTarget(out activity);
            return activity;
        }
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
    public void OnActivityDestroyed(Activity activity) { }
    public void OnActivityPaused(Activity activity) { }
    public void OnActivityResumed(Activity activity) => currentActivity = new WeakReference<Activity>(activity);
    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
}