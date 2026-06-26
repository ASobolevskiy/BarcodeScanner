namespace BarcodeScanner.Helpers;

internal class FrameThrottler(
    int delayBeforeScan,
    int delayBetweenFrames)
{
    private long _startTime = -1;
    private long _lastAnalyzed;

    public bool ShouldAnalyze(long currentTimeMs)
    {
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