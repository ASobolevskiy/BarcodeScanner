using System.Diagnostics;
using System.Threading;

namespace BarcodeScanner.Helpers;

// No internal synchronization by design - this is a per-frame hot path. Safe only because every
// real caller never invokes ShouldAnalyze() concurrently with itself on a given instance (Android:
// the single-thread CameraX analysis executor; iOS: the serial metadata dispatch queue's mutual-
// exclusion guarantee). Note this is NOT the same as "always the same physical thread" - GCD serial
// queues guarantee one block at a time, in order, but do not guarantee thread affinity across
// calls, so a check must only assert non-overlapping calls, never same-thread-every-time. Nothing
// here enforces that from inside the type; see EnterExclusiveRegion below for the Debug-only
// tripwire if that guarantee is ever broken.
internal class FrameThrottler(
    int delayBeforeScan,
    int delayBetweenFrames)
{
    private long _startTime = -1;
    private long _lastAnalyzed;

#if DEBUG
    private int _reentrancyGuard;

    private void EnterExclusiveRegion()
    {
        // The Interlocked.Exchange is deliberately its own statement, not inlined into the
        // Debug.Assert call: Debug.Assert carries [Conditional("DEBUG")], which makes the compiler
        // drop the entire call - arguments included - wherever DEBUG isn't defined. An inlined
        // Interlocked.Exchange would then silently stop running instead of just stopping asserting.
        var wasAlreadyInside = Interlocked.Exchange(ref _reentrancyGuard, 1) != 0;
        Debug.Assert(!wasAlreadyInside,
            "ShouldAnalyze was entered concurrently from more than one thread. This type has no " +
            "internal synchronization and relies entirely on the platform caller never invoking it " +
            "concurrently with itself (Android: the single-thread CameraX analysis executor; iOS: " +
            "the serial metadata queue).");
    }

    private void ExitExclusiveRegion() => Interlocked.Exchange(ref _reentrancyGuard, 0);
#endif

    /// <summary>
    /// Determines whether the current frame should be analyzed based on timing constraints.
    /// Uses monotonic time from TimeHelper to avoid issues with system clock changes.
    /// </summary>
    public bool ShouldAnalyze()
    {
#if DEBUG
        EnterExclusiveRegion();
        try
        {
#endif
        var currentTimeMs = TimeHelper.GetCurrentTimeMs();

        if (_startTime == -1)
            _startTime = currentTimeMs;

        if (currentTimeMs - _startTime < delayBeforeScan)
            return false;

        if (currentTimeMs - _lastAnalyzed < delayBetweenFrames)
            return false;

        _lastAnalyzed = currentTimeMs;
        return true;
#if DEBUG
        }
        finally
        {
            ExitExclusiveRegion();
        }
#endif
    }
}