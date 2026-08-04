using System.Diagnostics.CodeAnalysis;
using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Animations;
using BarcodeScanner.Enums;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;
using Path = Android.Graphics.Path;

namespace BarcodeScanner.Ui.Views;

internal class BarcodeScannerOverlayView : View, IActiveScannerOverlay, IDisposable
{
    #region Constants

    private const float GUIDE_LENGTH_DP = 25f;
    private const float GUIDE_RECT_WIDTH_DP = 250f;
    private const float GUIDE_RECT_HEIGHT_DP = 250f;
    private const float STROKE_WIDTH_DP = 2f;
    private const int ANIMATION_DURATION_MS = 300;

    #endregion

    #region Static corners parameters
    private float _guideLengthPx;
    private float _staticWidthPx;
    private float _staticHeightPx;
    private RectF? _staticRect;
    private readonly bool _showStaticGuides = true;
    private Paint _staticGuidesPaint;
    private Path? _staticGuidesPath;
    #endregion

    #region Active corners parameters

    private CornerPoints? _currentCorners;
    private CornerPoints? _targetCorners;
    private CornerPoints? _startCorners;
    private ValueAnimator? _animator;
    private Paint _activeGuidesPaint;

    #endregion

    private RoiRect? _externalRoi;
    private float _cachedLeft, _cachedTop, _cachedRight, _cachedBottom;
    private bool _boundsDirty = true;
    private ITimeInterpolator? _interpolator;

    #region Ctors

    protected BarcodeScannerOverlayView(
        IntPtr javaReference,
        JniHandleOwnership transfer
    ) : base(javaReference, transfer) => Init();

    public BarcodeScannerOverlayView(
        Context? context,
        IAttributeSet? attrs,
        int defStyleAttr,
        int defStyleRes
    ) : base(context, attrs, defStyleAttr, defStyleRes) => Init();

    public BarcodeScannerOverlayView(
        Context? context,
        IAttributeSet? attrs,
        int defStyleAttr
    ) : base(context, attrs, defStyleAttr) => Init();

    public BarcodeScannerOverlayView(
        Context? context,
        IAttributeSet? attrs
    ) : base(context, attrs) => Init();

    public BarcodeScannerOverlayView(
        Context? context
    ) : base(context) => Init();

    #endregion

    [MemberNotNull(nameof(_staticGuidesPaint), nameof(_activeGuidesPaint))]
    private void Init()
    {
        var density = Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        _guideLengthPx = GUIDE_LENGTH_DP * density;
        _staticWidthPx = GUIDE_RECT_WIDTH_DP * density;
        _staticHeightPx = GUIDE_RECT_HEIGHT_DP * density;
        var strokeWidthPx = STROKE_WIDTH_DP * density;
        
        _staticGuidesPaint = new Paint
        {
            Color = Color.White,
            StrokeWidth = strokeWidthPx,
            AntiAlias = true,
            StrokeCap = Paint.Cap.Round
        };
        _staticGuidesPaint.SetStyle(Paint.Style.Stroke);
        
        _activeGuidesPaint = new Paint
        {
            Color = Color.Green,
            StrokeWidth = strokeWidthPx,
            AntiAlias = true,
            StrokeCap = Paint.Cap.Round
        };
        _activeGuidesPaint.SetStyle(Paint.Style.Stroke);
        
        _interpolator = new AccelerateDecelerateInterpolator();
        
        _animator = ValueAnimator.OfFloat(0f, 1f);
        _animator?.SetDuration(ANIMATION_DURATION_MS);
        _animator?.SetInterpolator(_interpolator);
        _animator?.Update += OnAnimatorUpdate;
    }

    #region Lifecycle

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        ApplyLayout(w, h);
    }

    private void ApplyLayout(int w, int h)
    {
        _staticRect = _externalRoi is { IsValid: true } roi
            ? roi.ToRectF(w, h)
            : ComputeDefaultCenteredRect(w, h);
        
        _currentCorners ??= _staticRect.ToCornerPoints();
        _boundsDirty = true;
        BuildStaticGuidesPath();
    }

    private RectF ComputeDefaultCenteredRect(int w, int h)
    {
        var left = (w - _staticWidthPx) / 2f;
        var top = (h - _staticHeightPx) / 2f;
        
        return new RectF(left, 
                         top, 
                         left + _staticWidthPx, 
                         top + _staticHeightPx);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        if (_showStaticGuides && _targetCorners == null && _staticGuidesPath != null)
        {
            canvas.DrawPath(_staticGuidesPath, _staticGuidesPaint);
        }

        if (_targetCorners.HasValue)
        {
            DrawActiveGuides(canvas);
        }
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        _animator?.Cancel();
        _animator?.Update -= OnAnimatorUpdate;
        _animator?.Dispose();
        _animator = null;
    }

    #endregion

    #region Drawing

    private void BuildStaticGuidesPath()
    {
        if (_staticRect == null)
            return;
        
        _staticGuidesPath?.Dispose();
        _staticGuidesPath = new Path();

        var len = _guideLengthPx;
        var rect = _staticRect;
        
        // Top-left
        _staticGuidesPath.MoveTo(rect.Left, rect.Top + len);
        _staticGuidesPath.LineTo(rect.Left, rect.Top);
        _staticGuidesPath.LineTo(rect.Left + len, rect.Top);

        // Top-right
        _staticGuidesPath.MoveTo(rect.Right, rect.Top + len);
        _staticGuidesPath.LineTo(rect.Right, rect.Top);
        _staticGuidesPath.LineTo(rect.Right - len, rect.Top);

        // Bottom-right
        _staticGuidesPath.MoveTo(rect.Right, rect.Bottom - len);
        _staticGuidesPath.LineTo(rect.Right, rect.Bottom);
        _staticGuidesPath.LineTo(rect.Right - len, rect.Bottom);

        // Bottom-left
        _staticGuidesPath.MoveTo(rect.Left, rect.Bottom - len);
        _staticGuidesPath.LineTo(rect.Left, rect.Bottom);
        _staticGuidesPath.LineTo(rect.Left + len, rect.Bottom);
    }

    private void DrawActiveGuides(Canvas canvas)
    {
        if (_currentCorners is null)
        {
            Log.Warn("MobileBarcodeScanner", $"{nameof(_currentCorners)} is null in DrawActiveGuides");
            return;
        }

        var corners = _currentCorners.Value;

        if (_boundsDirty)
        {
            var bounds = corners.GetBounds();
            _cachedLeft = bounds.Left;
            _cachedTop = bounds.Top;
            _cachedRight = bounds.Right;
            _cachedBottom = bounds.Bottom;
            _boundsDirty = false;
        }
        
        var verticalLen = MathF.Min(_guideLengthPx, (_cachedBottom - _cachedTop) * 0.5f);
        var horizontalLen = MathF.Min(_guideLengthPx, (_cachedRight - _cachedLeft) * 0.5f);
        
        DrawCorner(canvas, corners.TopLeft.X, corners.TopLeft.Y, CornerType.TopLeft, verticalLen, horizontalLen, _activeGuidesPaint);
        DrawCorner(canvas, corners.TopRight.X, corners.TopRight.Y, CornerType.TopRight, verticalLen, horizontalLen, _activeGuidesPaint);
        DrawCorner(canvas, corners.BottomRight.X, corners.BottomRight.Y, CornerType.BottomRight, verticalLen, horizontalLen, _activeGuidesPaint);
        DrawCorner(canvas, corners.BottomLeft.X, corners.BottomLeft.Y, CornerType.BottomLeft, verticalLen, horizontalLen, _activeGuidesPaint);
    }

    private void DrawCorner(Canvas canvas, float x, float y, CornerType corner, float verticalLen, float horizontalLen,
        Paint paint)
    {
        var verticalSign = corner is CornerType.TopLeft or CornerType.TopRight ? 1f : -1f;
        var horizontalSign = corner is CornerType.TopLeft or CornerType.BottomLeft ? 1f : -1f;
        
        // Vertical line: from the "outer" point to the corner
        canvas.DrawLine(x, y + verticalLen * verticalSign, x, y, paint);
        // Horizontal line: from the corner to the "outer" point
        canvas.DrawLine(x, y, x + horizontalLen * horizontalSign, y, paint);
    }

    #endregion

    #region Public API
    public ViewFinderRect GetViewfinderRect() =>
        (_staticRect ?? new RectF(0, 0, 0, 0)).ToViewFinderRect();

    public void SyncRegionOfInterest(RoiRect roi)
    {
        _externalRoi = roi;
        if (Width <= 0 || Height <= 0) 
            return;
        
        ApplyLayout(Width, Height);
        PostInvalidate();
    }
    

    public void ClearOverlay()
    {
        _currentCorners = null;
        _targetCorners = null;
        _startCorners = null;
        _animator?.Cancel();
        _boundsDirty = true;
        Invalidate();
    }

    public void UpdateOverlay(string? barcodeValue, float[]? targetPoints)
    {
        if (targetPoints?.Length != 8)
        {
            Log.Warn("ScannerOverlay", $"Invalid targetPoints length: {targetPoints?.Length ?? 0}, expected 8");
            return;
        }

        if (_staticRect == null)
            return;
        
        _startCorners = _currentCorners ?? _staticRect.ToCornerPoints();
        _targetCorners = CornerPoints.FromFloatArray(targetPoints);
        
        _animator?.Cancel();
        _animator?.SetFloatValues(0f, 1f);
        _animator?.Start();
    }
    
    #endregion

    #region Animation

    private void OnAnimatorUpdate(object? sender, ValueAnimator.AnimatorUpdateEventArgs e)
    {
        if (_staticRect is null || _animator is null) 
            return;

        var currentTime = _animator.CurrentPlayTime;
        var duration = _animator.Duration;
        if (duration <= 0)
            return;
        
        var rawProgress = (float)currentTime/duration;
        var progress = _interpolator?.GetInterpolation(rawProgress) ?? rawProgress;
        progress = Math.Clamp(progress, 0f, 1f);

        var start = _startCorners ?? _staticRect.ToCornerPoints();
        var target = _targetCorners;
        if (!target.HasValue)
            return;
        _currentCorners = CornerPoints.Lerp(start, target.Value, progress);
        _boundsDirty = true;
        Invalidate();
    }

    #endregion

    #region Disposable implementation

    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        
        if (disposing)
        {
            _staticGuidesPaint.Dispose();
            _activeGuidesPaint.Dispose();
            _staticGuidesPath?.Dispose();

            _animator?.Cancel();
            _animator?.Update -= OnAnimatorUpdate;
            _animator?.Dispose();
            _interpolator?.Dispose();
        }
        
        base.Dispose(disposing);
        _disposed = true;
    }

#pragma warning disable CA1816
    void IDisposable.Dispose() => Dispose(true);
#pragma warning restore CA1816
    #endregion
}