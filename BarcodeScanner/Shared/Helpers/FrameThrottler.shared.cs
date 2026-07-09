namespace BarcodeScanner.Helpers;

internal class FrameThrottler(
    int delayBeforeScan,
    int delayBetweenFrames)
{
    private long _startTime = -1;
    private long _lastAnalyzed;

    /// <summary>
    /// Determines whether the current frame should be analyzed based on timing constraints.
    /// Uses monotonic time from TimeHelper to avoid issues with system clock changes.
    /// </summary>
    public bool ShouldAnalyze()
    {
        var currentTimeMs = TimeHelper.GetCurrentTimeMs();
        
        if (_startTime == -1)
            _startTime = currentTimeMs;
        
        if (currentTimeMs - _startTime < delayBeforeScan)
            return false;
        
        if (currentTimeMs - _lastAnalyzed < delayBetweenFrames)
            return false;
        
        _lastAnalyzed = currentTimeMs;
        return true;
    }
}