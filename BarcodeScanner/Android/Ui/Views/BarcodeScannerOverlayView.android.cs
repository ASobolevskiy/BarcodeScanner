using System.Diagnostics.CodeAnalysis;
using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Animations;
using BarcodeScanner.Helpers;
using BarcodeScanner.Shared.Enums;
using Path = Android.Graphics.Path;

namespace BarcodeScanner.Ui.Views;

public class BarcodeScannerOverlayView : View, IActiveScannerOverlay, IDisposable
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
    private ValueAnimator? _animator;
    private Paint _activeGuidesPaint;

    #endregion
    
    private float _cachedLeft, _cachedTop, _cachedRight, _cachedBottom;
    private bool _boundsDirty = true;

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
    }

    #region Lifecycle

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        
        var left = (w - _staticWidthPx) / 2f;
        var top = (h - _staticHeightPx) / 2f;
        _staticRect = new RectF(left, 
                                top, 
                                left + _staticWidthPx, 
                                top + _staticHeightPx);
        
        _currentCorners ??= CornerPoints.FromRect(_staticRect);
        _boundsDirty = true;
        BuildStaticGuidesPath();
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
        
        // Левый верхний
        _staticGuidesPath.MoveTo(rect.Left, rect.Top + len);
        _staticGuidesPath.LineTo(rect.Left, rect.Top);
        _staticGuidesPath.LineTo(rect.Left + len, rect.Top);

        // Правый верхний
        _staticGuidesPath.MoveTo(rect.Right, rect.Top + len);
        _staticGuidesPath.LineTo(rect.Right, rect.Top);
        _staticGuidesPath.LineTo(rect.Right - len, rect.Top);

        // Правый нижний
        _staticGuidesPath.MoveTo(rect.Right, rect.Bottom - len);
        _staticGuidesPath.LineTo(rect.Right, rect.Bottom);
        _staticGuidesPath.LineTo(rect.Right - len, rect.Bottom);

        // Левый нижний
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
        
        // Вертикальная линия: от "внешней" точки к углу
        canvas.DrawLine(x, y + verticalLen * verticalSign, x, y, paint);
        // Горизонтальная линия: от угла к "внешней" точке
        canvas.DrawLine(x, y, x + horizontalLen * horizontalSign, y, paint);
    }

    #endregion

    #region Public API

    public void ClearOverlay()
    {
        _currentCorners = null;
        _targetCorners = null;
        _animator?.Cancel();
        _boundsDirty = true;
        PostInvalidate();
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

        try
        {
            _animator?.Update -= OnAnimatorUpdate;
            _animator?.Cancel();
            _animator?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Java.Lang.Exception)
        {
            return;
        }
        
        _currentCorners ??= CornerPoints.FromRect(_staticRect);
        _targetCorners = CornerPoints.FromFloatArray(targetPoints);

        _animator = ValueAnimator.OfFloat(0f, 1f);
        _animator?.SetDuration(ANIMATION_DURATION_MS);
        _animator?.SetInterpolator(new AccelerateDecelerateInterpolator());
        _animator?.Update += OnAnimatorUpdate;
        _animator?.Start();
    }
    
    #endregion

    #region Animation

    private void OnAnimatorUpdate(object? sender, ValueAnimator.AnimatorUpdateEventArgs e)
    {
        if (e.Animation.AnimatedValue is not Java.Lang.Float progressObj || _staticRect == null) 
            return;
        
        var progress = progressObj.FloatValue();

        var start = _currentCorners ?? CornerPoints.FromRect(_staticRect);
        var target = _targetCorners;
        if (!target.HasValue)
            return;
        _currentCorners = CornerPoints.Lerp(start, target.Value, progress);
        _boundsDirty = true;
        PostInvalidate();
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
        }
        
        base.Dispose(disposing);
        _disposed = true;
    }

#pragma warning disable CA1816
    void IDisposable.Dispose() => Dispose(true);
#pragma warning restore CA1816
    #endregion
}