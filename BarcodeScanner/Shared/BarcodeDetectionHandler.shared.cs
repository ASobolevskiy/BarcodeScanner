using System.Collections.Concurrent;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;

namespace BarcodeScanner;

internal sealed class BarcodeDetectionHandler(
    BarcodeScanningOptions options)
{
    private const float SMOOTH_FACTOR_CENTER = 0.15f;
    private const float SMOOTH_FACTOR_SIZE = 0.3f;
    
    private readonly FrameThrottler _throttler = new(options.DelayBeforeAnalyzingFrames,
                                                     options.DelayBetweenAnalyzingFrames);
    private readonly int _delayBetweenScans = options.DelayBetweenContinuousScans;
    private readonly ScanType _scanType = options.ScannerMode;
    
    //private readonly ConcurrentDictionary<string, BarcodeBox> _lastBoxParams = new();
    private BarcodeBox? _lastBox;
    private string? _lastKey;
    
    private long _lastScannedTimeMs;
    private string? _lastSelectedBarcodeValue;
    
    public bool ShouldProcessFrame() => _throttler.ShouldAnalyze();

    public DetectionResult? Process(
        IEnumerable<BarcodeData> detectedBarcodes,
        RoiBounds roi)
    {
        var codesList = detectedBarcodes as IList<BarcodeData> ?? detectedBarcodes.ToList();

        if (codesList.Count is 0)
        {
            _lastScannedTimeMs = 0;
            _lastSelectedBarcodeValue = null;
            return new DetectionResult { ShouldResetOverlay = true };
        }

        var targetCode = SelectBestBarcode(codesList, roi);
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
    }
    
    private (BarcodeData Data, float Distance)? SelectBestBarcode(IList<BarcodeData> codes, RoiBounds roi)
    {
        var candidates = GetCandidates(codes, roi);

        if (candidates.Count == 0) 
            return null;

        if(string.IsNullOrWhiteSpace(_lastSelectedBarcodeValue)) 
            return candidates.OrderBy(c => c.Distance).First();
        
        var sticky = candidates.FirstOrDefault(c => 
                                                   c.Data.RawValue == _lastSelectedBarcodeValue);
        return sticky.Data.RawValue != null 
            ? sticky 
            : candidates.OrderBy(c => c.Distance).First();
    }

    private static List<(BarcodeData Data, float Distance)> GetCandidates(IList<BarcodeData> codes, RoiBounds roi)
    {
        var candidates = new List<(BarcodeData Data, float Distance)>();

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
            candidates.Add((code, distSq));
        }

        return candidates;
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


        //if (_lastBoxParams.TryGetValue(key, out var prevBox))
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
            //_lastBoxParams[key] = resultBox;
            _lastBox = resultBox;
            
            return resultBox.ToRectPoints();
        }

        //_lastBoxParams[key] = currentBox;
        _lastBox = currentBox;
        _lastKey = key;
        return currentBox.ToRectPoints();
    }
}