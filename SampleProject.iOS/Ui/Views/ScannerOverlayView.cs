using CoreAnimation;

namespace SampleProject.iOS;

public sealed class ScannerOverlayView : UIView
{
    private readonly CAShapeLayer _topLeftLayer;
    private readonly CAShapeLayer _topRightLayer;
    private readonly CAShapeLayer _bottomLeftLayer;
    private readonly CAShapeLayer _bottomRightLayer;

    private bool _hasInitializedViewfinder;
    
    public UIColor DefaultColor { get; set; } = UIColor.White;
    public UIColor DetectedColor { get; set; } = UIColor.SystemGreen;
    public float StrokeWidth { get; set; } = 3.0f;
    public float CornerLength { get; set; } = 24.0f; // Длина линии уголка
    public double AnimationDuration { get; set; } = 0.3; // Секунды
    
    // Текущее состояние
    private CGRect _currentFrame;
    private bool _isDetected;

    public ScannerOverlayView()
    {
        BackgroundColor = UIColor.Clear;
        UserInteractionEnabled = false;

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
            StrokeColor = DefaultColor.CGColor,
            LineWidth = StrokeWidth,
            FillColor = UIColor.Clear.CGColor,
            LineJoin = CAShapeLayer.JoinRound,
            LineCap = CAShapeLayer.CapRound,
            Frame = Bounds,
            Opacity = 0.9f
        };
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        
        if (!_hasInitializedViewfinder && Bounds.Width > 0 && Bounds.Height > 0)
        {
            ResetToViewfinder();
            _hasInitializedViewfinder = true;
        }
        
        _topLeftLayer.Frame = Bounds;
        _topRightLayer.Frame = Bounds;
        _bottomLeftLayer.Frame = Bounds;
        _bottomRightLayer.Frame = Bounds;
    }

    public void UpdateCorners(CGRect targetFrame, bool animate = false, bool isDetected = false)
    {
        _currentFrame = targetFrame;
        _isDetected = isDetected;
        
        var color = isDetected ? DetectedColor : DefaultColor;
        var targetColor = color.CGColor;
        
        var tlPath = CreateCornerPath(targetFrame, CornerPosition.TopLeft, CornerLength);
        var trPath = CreateCornerPath(targetFrame, CornerPosition.TopRight, CornerLength);
        var blPath = CreateCornerPath(targetFrame, CornerPosition.BottomLeft, CornerLength);
        var brPath = CreateCornerPath(targetFrame, CornerPosition.BottomRight, CornerLength);

        if (animate && AnimationDuration > 0)
        {
            AnimateLayer(_topLeftLayer, tlPath, targetColor);
            AnimateLayer(_topRightLayer, trPath, targetColor);
            AnimateLayer(_bottomLeftLayer, blPath, targetColor);
            AnimateLayer(_bottomRightLayer, brPath, targetColor);
        }
        else
        {
            _topLeftLayer.Path = tlPath;
            _topRightLayer.Path = trPath;
            _bottomLeftLayer.Path = blPath;
            _bottomRightLayer.Path = brPath;
            _topLeftLayer.StrokeColor = targetColor;
            _topRightLayer.StrokeColor = targetColor;
            _bottomLeftLayer.StrokeColor = targetColor;
            _bottomRightLayer.StrokeColor = targetColor;
        }
    }

    private CGPath CreateCornerPath(CGRect frame, CornerPosition position, float length)
    {
        var x = (float)frame.X;
        var y = (float)frame.Y;
        var w = (float)frame.Width;
        var h = (float)frame.Height;

        var path = new CGPath();

        switch (position)
        {
            case CornerPosition.TopLeft:
                path.MoveToPoint(x, y + length);
                path.AddLineToPoint(x, y);
                path.AddLineToPoint(x + length, y);
                break;
            case CornerPosition.TopRight:
                path.MoveToPoint(x + w - length, y);
                path.AddLineToPoint(x + w, y);
                path.AddLineToPoint(x + w, y + length);
                break;
            
            case CornerPosition.BottomLeft:
                path.MoveToPoint(x, y + h - length);
                path.AddLineToPoint(x, y + h);
                path.AddLineToPoint(x + length, y + h);
                break;
            
            case CornerPosition.BottomRight:
                path.MoveToPoint(x + w - length, y + h);
                path.AddLineToPoint(x + w, y + h);
                path.AddLineToPoint(x + w, y + h - length);
                break;
        }
        return path;
    }

    private void AnimateLayer(CAShapeLayer layer, CGPath targetPath, CGColor targetColor)
    {
        var pathAnim = CABasicAnimation.FromKeyPath("path");
        pathAnim.Duration = AnimationDuration;
        pathAnim.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
        pathAnim.FillMode = CAFillMode.Forwards;
        pathAnim.RemovedOnCompletion = false;
        pathAnim.To = FromObject(targetPath);
        
        var colorAnim = CABasicAnimation.FromKeyPath("strokeColor");
        colorAnim.Duration = AnimationDuration;
        colorAnim.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
        colorAnim.FillMode = CAFillMode.Forwards;
        colorAnim.RemovedOnCompletion = false;
        colorAnim.To = FromObject(targetColor);
        
        layer.AddAnimation(pathAnim, "pathAnim");
        layer.AddAnimation(colorAnim, "colorAnim");
    }
    
    public void ResetToViewfinder()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        
        var padding = Math.Max(Bounds.Width, Bounds.Height) * 0.15f;
    
        var availableWidth = Bounds.Width - padding * 2;
        var availableHeight = Bounds.Height - padding * 2;
    
        var squareSize = Math.Min(availableWidth, availableHeight);
    
        var offsetX = (availableWidth - squareSize) / 2;
        var offsetY = (availableHeight - squareSize) / 2;
    
        var viewfinder = new CGRect(
                                    padding + offsetX,
                                    padding + offsetY,
                                    squareSize,
                                    squareSize);
    
        UpdateCorners(viewfinder, animate: false, isDetected: false);
    }
    
    public void ForceResetViewfinder()
    {
        _hasInitializedViewfinder = false;
        SetNeedsLayout(); // Запросить перерасчёт макета
        LayoutIfNeeded(); // Применить немедленно
    }
}

public enum CornerPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}