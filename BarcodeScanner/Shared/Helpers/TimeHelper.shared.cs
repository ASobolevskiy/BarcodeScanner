using System.Diagnostics;

namespace BarcodeScanner.Helpers;

/// <summary>
/// Provides monotonic time utilities for measuring intervals.
/// Uses Stopwatch.GetTimestamp() which is guaranteed to be monotonic
/// (never goes backward, unlike system clock).
/// </summary>
internal static class TimeHelper
{
    /// <summary>
    /// Conversion factor from Stopwatch ticks to milliseconds.
    /// Computed once at startup for performance.
    /// </summary>
    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
    
    /// <summary>
    /// Gets the current monotonic time in milliseconds.
    /// This value is guaranteed to never go backward, even if system clock changes
    /// (NTP sync, manual time change, timezone change).
    /// </summary>
    public static long GetCurrentTimeMs() => (long)(Stopwatch.GetTimestamp() * MsPerTick);
}