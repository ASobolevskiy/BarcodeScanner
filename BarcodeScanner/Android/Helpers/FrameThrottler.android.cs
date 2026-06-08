namespace BarcodeScanner.Helpers;

public class FrameThrottler(
    int delayBeforeScan,
    int delayBetweenFrames)
{
    private readonly long _startTime = Android.OS.SystemClock.ElapsedRealtime();
    private long _lastAnalyzed;

    public bool ShouldAnalyze()
    {
        var currentTime = Android.OS.SystemClock.ElapsedRealtime();

        if (currentTime - _startTime < delayBeforeScan)
            return false;

        if (currentTime - _lastAnalyzed < delayBetweenFrames)
            return false;
        
        _lastAnalyzed = currentTime;
        return true;
    }
}