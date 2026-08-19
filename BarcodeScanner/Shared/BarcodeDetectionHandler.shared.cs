using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;

namespace BarcodeScanner;

// No internal synchronization by design - Process() runs on a per-frame hot path. Safe only
// because every real caller never invokes Process() concurrently with itself on a given instance
// (Android: RunOnUiThread; iOS: the same serial metadata dispatch queue's mutual-exclusion
// guarantee that also drives ShouldProcessFrame). Note this is NOT "always the same physical
// thread" - GCD serial queues guarantee one block at a time, not thread affinity across calls -
// and Process()/ShouldProcessFrame() are NOT required to share a thread with each other either:
// on Android they intentionally run on different ones (analysis executor vs UI thread). Each just
// needs to never overlap with itself. Nothing here enforces that from inside the type; see
// EnterExclusiveRegion below for the Debug-only tripwire if that guarantee is ever broken.
internal sealed class BarcodeDetectionHandler(
    BarcodeScanningOptions options)
{
    private const float SMOOTH_FACTOR_CENTER = 0.15f;
    private const float SMOOTH_FACTOR_SIZE = 0.3f;

    private readonly FrameThrottler _throttler = new(options.DelayBeforeAnalyzingFramesMs,
                                                     options.DelayBetweenAnalyzingFramesMs);
    private readonly int _delayBetweenScans = options.DelayBetweenContinuousScansMs;
    private readonly ScanType _scanType = options.ScannerMode;

    private readonly List<(BarcodeData Data, float Distance)> _candidatesBuffer = [];
    private BarcodeBox? _lastBox;
    private string? _lastKey;

    private long _lastScannedTimeMs;
    private string? _lastSelectedBarcodeValue;

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
            "Process was entered concurrently from more than one thread. This type has no " +
            "internal synchronization and relies entirely on the platform caller never invoking " +
            "it concurrently with itself (Android: RunOnUiThread; iOS: the serial metadata queue).");
    }

    private void ExitExclusiveRegion() => Interlocked.Exchange(ref _reentrancyGuard, 0);
#endif

    public bool ShouldProcessFrame() => _throttler.ShouldAnalyze();

    public DetectionResult? Process(
        IList<BarcodeData> detectedBarcodes,
        RoiBounds roi)
    {
#if DEBUG
        EnterExclusiveRegion();
        try
        {
#endif
        if (detectedBarcodes.Count is 0)
        {
            _lastScannedTimeMs = 0;
            _lastSelectedBarcodeValue = null;
            return new DetectionResult { ShouldResetOverlay = true };
        }

        var targetCode = SelectBestBarcode(detectedBarcodes, roi);
        if (targetCode is null) return null;

        var currentTimeMs = TimeHelper.GetCurrentTimeMs();
        if (_scanType == ScanType.Continuous && currentTimeMs - _lastScannedTimeMs < _delayBetweenScans)
            return null;

        var barcodeData = targetCode.Value.Data;
        if (string.IsNullOrWhiteSpace(barcodeData.RawValue))
            return null;

        _lastScannedTimeMs = currentTimeMs;
        _lastSelectedBarcodeValue = barcodeData.RawValue;

        var smoothedPoints = ApplySmoothing(barcodeData.RawValue, barcodeData.ScreenCornerPoints, roi);

        return new DetectionResult
        {
            RawValue = barcodeData.RawValue,
            DisplayValue = string.IsNullOrWhiteSpace(barcodeData.DisplayValue) ? barcodeData.RawValue : barcodeData.DisplayValue,
            Symbology = barcodeData.Symbology,
            SmoothedPoints = smoothedPoints,
            ScanType = _scanType
        };
#if DEBUG
        }
        finally
        {
            ExitExclusiveRegion();
        }
#endif
    }
    
    private (BarcodeData Data, float Distance)? SelectBestBarcode(IList<BarcodeData> codes, RoiBounds roi)
    {
        _candidatesBuffer.Clear();
        GetCandidates(codes, roi);

        if (_candidatesBuffer.Count == 0) 
            return null;

        if(string.IsNullOrWhiteSpace(_lastSelectedBarcodeValue)) 
            return FindMinByDistance();
        
        for (var i = 0; i < _candidatesBuffer.Count; i++)
        {
            if (_candidatesBuffer[i].Data.RawValue == _lastSelectedBarcodeValue)
                return _candidatesBuffer[i];
        }
        
        return FindMinByDistance();
    }

    private void GetCandidates(IList<BarcodeData> codes, RoiBounds roi)
    {
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code.RawValue)) 
                continue;
            
            var points = code.ScreenCornerPoints;
            if (points is null or { Length: < 8 }) 
                continue;
            
            var center = GetBarcodeCenter(points);
            if (!roi.Contains(center.X, center.Y)) 
                continue;
            
            var distSq = CalculateDistance(center, roi);
            _candidatesBuffer.Add((code, distSq));
        }
    }
    
    private (BarcodeData Data, float Distance) FindMinByDistance()
    {
        var min = _candidatesBuffer[0];
        for (var i = 1; i < _candidatesBuffer.Count; i++)
        {
            if (_candidatesBuffer[i].Distance < min.Distance)
                min = _candidatesBuffer[i];
        }
        return min;
    }
    
    private static (float X, float Y) GetBarcodeCenter(float[] points)
    {
        float cx = 0, cy = 0;
        for (var i = 0; i < 8; i += 2) { cx += points[i]; cy += points[i + 1]; }
        cx *= 0.25f; cy *= 0.25f;
        return (cx, cy);
    }

    private static float CalculateDistance((float X, float Y) center, RoiBounds roi)
    {
        var x = (center.X - roi.CenterX) * (center.X - roi.CenterX);
        var y = (center.Y - roi.CenterY) * (center.Y - roi.CenterY);
        return x + y;
    }
    
    private float[] ApplySmoothing(string key, float[] rawPoints, RoiBounds roi)
    {
        var minWidth = Math.Max(roi.Width * 0.1f, 60f);
        var minHeight = Math.Max(roi.Height * 0.1f, 60f);;
        
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i += 2)
        {
            var x = rawPoints[i]; var y = rawPoints[i + 1];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        var curCenterX = (minX + maxX) * 0.5f;
        var curCenterY = (minY + maxY) * 0.5f;
        var curWidth = Math.Max(maxX - minX, minWidth);
        var curHeight = Math.Max(maxY - minY, minHeight);

        var currentBox = new BarcodeBox(curCenterX, curCenterY, curWidth, curHeight);
        
        if(_lastKey == key && _lastBox.HasValue)
        {
            var prevBox = _lastBox.Value;
            var dist = MathF.Sqrt((curCenterX - prevBox.CenterX) * (curCenterX - prevBox.CenterX) +
                                  (curCenterY - prevBox.CenterY) * (curCenterY - prevBox.CenterY));
            
            var maxDist = curWidth * 1.5f; 
            var offsetWhole = Math.Clamp(dist / maxDist, 0f, 1f);
            var centerSmoothFactor = SMOOTH_FACTOR_CENTER + offsetWhole * 0.85f;

            var scx = prevBox.CenterX + (curCenterX - prevBox.CenterX) * centerSmoothFactor;
            var scy = prevBox.CenterY + (curCenterY - prevBox.CenterY) * centerSmoothFactor;
            var sw = prevBox.Width + (curWidth - prevBox.Width) * SMOOTH_FACTOR_SIZE;
            var sh = prevBox.Height + (curHeight - prevBox.Height) * SMOOTH_FACTOR_SIZE;

            var resultBox = new BarcodeBox(scx, scy, sw, sh);
            _lastBox = resultBox;
            
            return resultBox.ToRectPoints();
        }

        _lastBox = currentBox;
        _lastKey = key;
        return currentBox.ToRectPoints();
    }
}