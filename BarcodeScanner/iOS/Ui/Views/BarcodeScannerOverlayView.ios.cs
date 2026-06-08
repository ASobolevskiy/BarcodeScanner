using BarcodeScanner.Shared.Enums;
using CoreAnimation;

namespace BarcodeScanner.iOS.Ui.Views;

public sealed class BarcodeScannerOverlayView : UIView
{
    private readonly CAShapeLayer _topLeftLayer;
    private readonly CAShapeLayer _topRightLayer;
    private readonly CAShapeLayer _bottomLeftLayer;
    private readonly CAShapeLayer _bottomRightLayer;

    private bool _hasInitializedViewfinder;
    private bool _disposed;

    private (CGRect frame, bool detected, float cornerLength) _cachedState;
    private CGPath? _cachedTl, _cachedTr, _cachedBl, _cachedBr;

    public UIColor DefaultColor { get; set; } = UIColor.White;
    public UIColor DetectedColor { get; set; } = UIColor.SystemGreen;
    public float StrokeWidth { get; set; } = 3.0f;
    public float CornerLength { get; set; } = 24.0f;
    public double AnimationDuration { get; set; } = 0.3;
    public bool EnablePathCaching { get; set; } = true;
    
    public BarcodeScannerOverlayView()
    {
        BackgroundColor = UIColor.Clear;
        UserInteractionEnabled = false;
        IsAccessibilityElement = false;

        _topLeftLayer = CreateCornerLayer();
        _topRightLayer = CreateCornerLayer();
        _bottomLeftLayer = CreateCornerLayer();
        _bottomRightLayer = CreateCornerLayer();
        
        Layer.AddSublayer(_topLeftLayer);
        Layer.AddSublayer(_topRightLayer);
        Layer.AddSublayer(_bottomLeftLayer);
        Layer.AddSublayer(_bottomRightLayer);
    }
    
    private CAShapeLayer CreateCornerLayer()
    {
        return new CAShapeLayer
        {
            FillColor = UIColor.Clear.CGColor,
            LineJoin = CAShapeLayer.JoinRound,
            LineCap = CAShapeLayer.CapRound,
            Frame = Bounds,
            Opacity = 0.9f,
            ContentsScale = UIScreen.MainScreen.Scale,
            ShadowOpacity = 0,
            ShouldRasterize = false
        };
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        if (!_hasInitializedViewfinder && !Bounds.IsEmpty)
        {
            ResetToViewfinder();
            _hasInitializedViewfinder = true;
        }

        if (_topLeftLayer.Frame.Equals(Bounds)) 
            return;
        
        _topLeftLayer.Frame = Bounds;
        _topRightLayer.Frame = Bounds;
        _bottomLeftLayer.Frame = Bounds;
        _bottomRightLayer.Frame = Bounds;
    }

    public void ResetToViewfinder()
    {
        if (Bounds.IsEmpty)
            return;
        
        const float paddingRatio = 0.15f;
        var padding = Math.Max(Bounds.Width, Bounds.Height) * paddingRatio;
        
        var innerWidth = Bounds.Width - 2 * padding;
        var innerHeight = Bounds.Height - 2 * padding;
        var squareSize = Math.Min(innerWidth, innerHeight);
        
        var x = (Bounds.Width - squareSize) / 2;
        var y = (Bounds.Height - squareSize) / 2;
        
        var viewfinder = new CGRect(x, y, squareSize, squareSize);
        UpdateCorners(viewfinder, animate: false, isDetected: false);
    }
    
    public void ForceResetViewfinder()
    {
        _hasInitializedViewfinder = false;
        SetNeedsLayout();
        LayoutIfNeeded();
    }

    public void UpdateCorners(CGRect targetFrame, bool animate = false, bool isDetected = false)
    {
        if (!NSThread.IsMain)
        {
            BeginInvokeOnMainThread(() => UpdateCorners(targetFrame, animate, isDetected));
            return;
        }

        if (EnablePathCaching &&
            _cachedState.frame == targetFrame &&
            _cachedState.detected == isDetected &&
            _cachedState.cornerLength == CornerLength &&
            _cachedTl != null)
        {
            ApplyCachedPaths(isDetected, animate);
            return;
        }
        
        var tlPath = CreateCornerPath(targetFrame, CornerType.TopLeft);
        var trPath = CreateCornerPath(targetFrame, CornerType.TopRight);
        var blPath = CreateCornerPath(targetFrame, CornerType.BottomLeft);
        var brPath = CreateCornerPath(targetFrame, CornerType.BottomRight);
        
        if (EnablePathCaching)
        {
            _cachedTl = tlPath;
            _cachedTr = trPath;
            _cachedBl = blPath;
            _cachedBr = brPath;
            _cachedState = (targetFrame, isDetected, CornerLength);
        }

        ApplyPaths(tlPath, trPath, blPath, brPath, isDetected, animate);
    }
    
    private void ApplyCachedPaths(bool isDetected, bool animate)
    {
        if (_cachedTl == null) return;
        ApplyPaths(_cachedTl, _cachedTr, _cachedBl, _cachedBr, isDetected, animate);
    }

    private void ApplyPaths(CGPath? tl, CGPath? tr, CGPath? bl, CGPath? br, bool isDetected, bool animate)
    {
        if (tl == null || tr == null || bl == null || br == null)
            return;
        
        var targetColor = (isDetected ? DetectedColor : DefaultColor).CGColor;

        if (animate && AnimationDuration > 0)
        {
            AnimateLayer(_topLeftLayer, tl, targetColor);
            AnimateLayer(_topRightLayer, tr, targetColor);
            AnimateLayer(_bottomLeftLayer, bl, targetColor);
            AnimateLayer(_bottomRightLayer, br, targetColor);
        }
        else
        {
            _topLeftLayer.RemoveAllAnimations();
            _topRightLayer.RemoveAllAnimations();
            _bottomLeftLayer.RemoveAllAnimations();
            _bottomRightLayer.RemoveAllAnimations();
            
            _topLeftLayer.Path = tl;
            _topRightLayer.Path = tr;
            _bottomLeftLayer.Path = bl;
            _bottomRightLayer.Path = br;
            _topLeftLayer.StrokeColor = targetColor;
            _topRightLayer.StrokeColor = targetColor;
            _bottomLeftLayer.StrokeColor = targetColor;
            _bottomRightLayer.StrokeColor = targetColor;
        }
    }

    private CGPath CreateCornerPath(CGRect frame, CornerType position)
    {
        var path = new CGPath();
        var x = (float)frame.X;
        var y = (float)frame.Y;
        var w = (float)frame.Width;
        var h = (float)frame.Height;
        var len = CornerLength;

        switch (position)
        {
            case CornerType.TopLeft:
                path.MoveToPoint(x, y + len);
                path.AddLineToPoint(x, y);
                path.AddLineToPoint(x + len, y);
                break;
            case CornerType.TopRight:
                path.MoveToPoint(x + w - len, y);
                path.AddLineToPoint(x + w, y);
                path.AddLineToPoint(x + w, y + len);
                break;
            case CornerType.BottomLeft:
                path.MoveToPoint(x, y + h - len);
                path.AddLineToPoint(x, y + h);
                path.AddLineToPoint(x + len, y + h);
                break;
            case CornerType.BottomRight:
                path.MoveToPoint(x + w - len, y + h);
                path.AddLineToPoint(x + w, y + h);
                path.AddLineToPoint(x + w, y + h - len);
                break;
        }

        return path;
    }

    private void AnimateLayer(CAShapeLayer layer, CGPath targetPath, CGColor targetColor)
    {
        layer.Path = targetPath;
        layer.StrokeColor = targetColor;
        
        var pathAnim = CABasicAnimation.FromKeyPath("path");
        pathAnim.To = FromObject(targetPath);
        
        var colorAnim = CABasicAnimation.FromKeyPath("strokeColor");
        colorAnim.To = FromObject(targetColor);
        
        var group = new CAAnimationGroup
        {
            Animations = [pathAnim, colorAnim],
            Duration = AnimationDuration,
            TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut),
            RemovedOnCompletion = true,
            FillMode = CAFillMode.Removed
        };

        layer.AddAnimation(group, null);
    }
    
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        RemoveLayerAnimations();
        RemoveLayersFromSuper();
        DisposeLayers();
        DisposeCachedPaths();

        _disposed = true;
    }
    
    private void RemoveLayerAnimations()
    {
        _topLeftLayer?.RemoveAllAnimations();
        _topRightLayer?.RemoveAllAnimations();
        _bottomLeftLayer?.RemoveAllAnimations();
        _bottomRightLayer?.RemoveAllAnimations();
    }

    private void RemoveLayersFromSuper()
    {
        _topLeftLayer?.RemoveFromSuperLayer();
        _topRightLayer?.RemoveFromSuperLayer();
        _bottomLeftLayer?.RemoveFromSuperLayer();
        _bottomRightLayer?.RemoveFromSuperLayer();
    }

    private void DisposeLayers()
    {
        _topLeftLayer?.Dispose();
        _topRightLayer?.Dispose();
        _bottomLeftLayer?.Dispose();
        _bottomRightLayer?.Dispose();
    }

    private void DisposeCachedPaths()
    {
        _cachedTl?.Dispose();
        _cachedTr?.Dispose();
        _cachedBl?.Dispose();
        _cachedBr?.Dispose();
        _cachedTl = null;
        _cachedTr = null;
        _cachedBl = null;
        _cachedBr = null;
    }
}