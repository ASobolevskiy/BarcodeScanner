namespace BarcodeScanner.Helpers;

internal class ActivityLifecycleTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private static WeakReference<Activity>? currentActivity;

    public static Activity? CurrentActivity
    {
        get
        {
            Activity? activity = null;
            Volatile.Read(ref currentActivity)?.TryGetTarget(out activity);
            return activity;
        }
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
    public void OnActivityDestroyed(Activity activity) => ClearIfCurrent(activity);
    public void OnActivityPaused(Activity activity) { }
    public void OnActivityResumed(Activity activity) => Volatile.Write(ref currentActivity, new WeakReference<Activity>(activity));
    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) => ClearIfCurrent(activity);

    // Only clears the tracked reference if it's still the activity going away - not an
    // unconditional clear. Two things this guards against:
    // - Android guarantees A.OnPause() -> B.OnResume() -> A.OnStop() when A starts B (our own
    //   scanner launch included) - A's OnStop fires AFTER B has already become current, so an
    //   unconditional clear here would wipe out B's now-correct reference.
    // - Comparing by reference instead of trusting callback ordering keeps this correct
    //   regardless of any OEM/multi-window scenario where that ordering might differ.
    //
    // OnPause is deliberately NOT wired to this. Pausing (unlike stopping) is not a reliable
    // signal that the activity is no longer usable: a permission dialog or share sheet from
    // another app pauses the host activity without any Activity of ours ever resuming to take
    // its place, and starting a new activity from a merely-paused one is normal, supported
    // Android usage. Clearing on pause would make CurrentActivity spuriously unavailable during
    // exactly those common interruptions, which work fine today.
    private static void ClearIfCurrent(Activity activity)
    {
        var current = Volatile.Read(ref currentActivity);
        if (current != null && current.TryGetTarget(out var target) && ReferenceEquals(target, activity))
            Volatile.Write(ref currentActivity, null);
    }
}