using System.Linq;
using AVFoundation;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;

namespace BarcodeScanner;

public class BarcodeDetectionHandler(BarcodeScanningOptions options)
{
    private readonly FrameThrottler _throttler = new(options.DelayBeforeAnalyzingFrames,
                                                     options.DelayBetweenAnalyzingFrames);
    private readonly int _delayBetweenScans = options.DelayBetweenContinuousScans;
    private readonly bool _isContinuousScan = options.ScannerMode == BarcodeScanningOptions.ScanType.Continuous;
    
    private long _lastScannedTimeMs;

    internal DetectionResult? ProcessCode(
        AVMetadataObject[]? metadataObjects, 
        long currentTimeMs,
        AVCaptureVideoPreviewLayer? previewLayer)
    {
        if (!_throttler.ShouldAnalyze(currentTimeMs))
            return null;
        
        if (metadataObjects == null || metadataObjects.Length == 0)
        {
            _lastScannedTimeMs = 0; 
            return new DetectionResult { ShouldResetOverlay = true };
        }
        
        var codes = metadataObjects.Cast<AVMetadataMachineReadableCodeObject>().ToArray();
        if (codes.Length == 0)
        {
            _lastScannedTimeMs = 0; 
            return new DetectionResult { ShouldResetOverlay = true };
        }
        
        var firstCode = codes[0];
        if (string.IsNullOrEmpty(firstCode.StringValue)) return null;

        if (_isContinuousScan && currentTimeMs - _lastScannedTimeMs < _delayBetweenScans)
            return null;
        
        _lastScannedTimeMs = currentTimeMs;

        float[]? points = null;
        if (previewLayer != null)
        {
            var transformed = previewLayer.GetTransformedMetadataObject(firstCode) as AVMetadataMachineReadableCodeObject;
            points = ApplyMinimumHeight(transformed?.Corners).ToFloatArray();
        }

        return new DetectionResult
        {
            StringValue = firstCode.StringValue,
            Format = firstCode.Type,
            SmoothedPoints = points,
            ShouldResetOverlay = false
        };
    }
    
    private CGPoint[]? ApplyMinimumHeight(CGPoint[]? corners)
    {
        if (corners == null || corners.Length < 4) 
            return null;
    
        float minHeight = 50;
        
        var x = corners.Min(p => p.X);
        var y = corners.Min(p => p.Y);
        var w = corners.Max(p => p.X) - x;
        var h = corners.Max(p => p.Y) - y;
    
        var finalH = Math.Max(h, minHeight);
        
        var rect = new CGRect(x, y, w, finalH);


        return rect.ToCgPointArray();
    }
}

internal readonly record struct DetectionResult
{
    public string? StringValue { get; init; }
    public AVMetadataObjectType Format { get; init; }
    public float[]? SmoothedPoints { get; init; }
    public bool ShouldResetOverlay { get; init; }
}