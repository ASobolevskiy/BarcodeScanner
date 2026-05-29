using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;

namespace SampleProject.Droid;

public class ScannerOverlayView : View
{
    //Static guides
    private const float GUIDE_LENGTH = 40f;
    private const float GUIDE_RECT_WIDTH = 250;
    private const float GUIDE_RECT_HEIGHT = 250;
    private float _staticWidth;
    private float _staticHeight;
    private RectF? _staticRect;
    private readonly bool _showStaticGuides = true;
    private Paint? _staticGuidesPaint;

    //Animated guides
    private float[]? _currentCornerPoints;
    private float[]? _targetCornerPoints;
    private ValueAnimator? _animator;
    private Paint? _activeGuidesPaint;
    
    protected ScannerOverlayView(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
        Init();
    }

    public ScannerOverlayView(Context? context, IAttributeSet? attrs, int defStyleAttr, int defStyleRes) : base(context, attrs, defStyleAttr, defStyleRes)
    {
        Init();
    }

    public ScannerOverlayView(Context? context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        Init();
    }

    public ScannerOverlayView(Context? context, IAttributeSet? attrs) : base(context, attrs)
    {
        Init();
    }

    public ScannerOverlayView(Context? context) : base(context)
    {
        Init();
    }

    private void Init()
    {
        _staticHeight = DpToPx(GUIDE_RECT_HEIGHT);
        _staticWidth = DpToPx(GUIDE_RECT_WIDTH);
        
        _staticGuidesPaint = new Paint
        {
            Color = Color.White,
            StrokeWidth = 6f,
            AntiAlias = true,
            StrokeCap = Paint.Cap.Round
        };
        _staticGuidesPaint.SetStyle(Paint.Style.Stroke);

        _activeGuidesPaint = new Paint
        {
            Color = Color.Green,
            StrokeWidth = 6f,
            AntiAlias = true,
            StrokeCap = Paint.Cap.Round
        };
        _staticGuidesPaint.SetStyle(Paint.Style.Stroke);
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        var left = (w - _staticWidth) / 2f;
        var top = (h - _staticHeight) / 2f;
        _staticRect = new RectF(left, top, left + _staticWidth, top + _staticHeight);
        
        _currentCornerPoints ??= RectToCornerPoints(_staticRect);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_showStaticGuides && _targetCornerPoints == null)
        {
            DrawStaticGuides(canvas);
        }

        if (_targetCornerPoints != null && _currentCornerPoints is { Length: >= 8 })
        {
            DrawActiveGuides(canvas);
        }
    }

    private void DrawStaticGuides(Canvas canvas)
    {
        if (_staticRect == null || _staticGuidesPaint == null)
            return;
        
        // Левый верхний
        canvas.DrawLine(_staticRect.Left, _staticRect.Top + GUIDE_LENGTH,
                        _staticRect.Left, _staticRect.Top, _staticGuidesPaint);
        canvas.DrawLine(_staticRect.Left, _staticRect.Top,
                        _staticRect.Left + GUIDE_LENGTH, _staticRect.Top, _staticGuidesPaint);
        // Правый верхний
        canvas.DrawLine(_staticRect.Right, _staticRect.Top + GUIDE_LENGTH,
                        _staticRect.Right, _staticRect.Top, _staticGuidesPaint);
        canvas.DrawLine(_staticRect.Right, _staticRect.Top,
                        _staticRect.Right - GUIDE_LENGTH, _staticRect.Top, _staticGuidesPaint);
        // Правый нижний
        canvas.DrawLine(_staticRect.Right, _staticRect.Bottom - GUIDE_LENGTH,
                        _staticRect.Right, _staticRect.Bottom, _staticGuidesPaint);
        canvas.DrawLine(_staticRect.Right, _staticRect.Bottom,
                        _staticRect.Right - GUIDE_LENGTH, _staticRect.Bottom, _staticGuidesPaint);
        // Левый нижний
        canvas.DrawLine(_staticRect.Left, _staticRect.Bottom - GUIDE_LENGTH,
                        _staticRect.Left, _staticRect.Bottom, _staticGuidesPaint);
        canvas.DrawLine(_staticRect.Left, _staticRect.Bottom,
                        _staticRect.Left + GUIDE_LENGTH, _staticRect.Bottom, _staticGuidesPaint);
    }

    private void DrawActiveGuides(Canvas canvas)
    {
        if (_currentCornerPoints is not { Length: >= 8 } || _activeGuidesPaint == null)
            return;
        
        var left = Math.Min(Math.Min(_currentCornerPoints[0], _currentCornerPoints[2]), 
                              Math.Min(_currentCornerPoints[4], _currentCornerPoints[6]));
        var right = Math.Max(Math.Max(_currentCornerPoints[0], _currentCornerPoints[2]), 
                               Math.Max(_currentCornerPoints[4], _currentCornerPoints[6]));
        var top = Math.Min(Math.Min(_currentCornerPoints[1], _currentCornerPoints[3]), 
                             Math.Min(_currentCornerPoints[5], _currentCornerPoints[7]));
        var bottom = Math.Max(Math.Max(_currentCornerPoints[1], _currentCornerPoints[3]), 
                                Math.Max(_currentCornerPoints[5], _currentCornerPoints[7]));
        
        var verticalLen = Math.Min(GUIDE_LENGTH, (bottom - top) * 0.5f);
        var horizontalLen = Math.Min(GUIDE_LENGTH, (right - left) * 0.5f);
        
        // Левый верхний
        canvas.DrawLine(left, top + verticalLen, left, top, _activeGuidesPaint);
        canvas.DrawLine(left, top, left + horizontalLen, top, _activeGuidesPaint);
        // Правый верхний
        canvas.DrawLine(right, top + verticalLen, right, top, _activeGuidesPaint);
        canvas.DrawLine(right, top, right - horizontalLen, top, _activeGuidesPaint);
        // Правый нижний
        canvas.DrawLine(right, bottom - verticalLen, right, bottom, _activeGuidesPaint);
        canvas.DrawLine(right, bottom, right - horizontalLen, bottom, _activeGuidesPaint);
        // Левый нижний
        canvas.DrawLine(left, bottom - verticalLen, left, bottom, _activeGuidesPaint);
        canvas.DrawLine(left, bottom, left + horizontalLen, bottom, _activeGuidesPaint);
    }

    public void ClearOverlay()
    {
        _currentCornerPoints = null;
        _targetCornerPoints = null;
        _animator?.Cancel();
        PostInvalidate();
    }

    public void UpdateBarcode(string? barcodeValue, float[] targetPoints)
    {
        if (targetPoints is not { Length: >= 8 } || _staticRect == null)
            return;
        
        _animator?.Cancel();

        var startPoints = _currentCornerPoints ?? RectToCornerPoints(_staticRect);
        _targetCornerPoints = targetPoints;

        _animator = ValueAnimator.OfFloat(0f, 1f);
        _animator?.SetDuration(300);
        _animator?.Update += (_, e) =>
        {
            var fProgress = 1f;
            if (e.Animation.AnimatedValue is Java.Lang.Float progress)
            {
                fProgress = progress.FloatValue();
            }
            
            var interp = new float[8];
            for (var i = 0; i < 8; i++)
            {
                interp[i] = startPoints[i] + (targetPoints[i] - startPoints[i]) * fProgress;
            }
            _currentCornerPoints = interp;
            PostInvalidate();
        };
        _animator?.Start();
    }

    private float DpToPx(float dp) => dp * (Context?.Resources?.DisplayMetrics?.Density ?? 1);

    private float[] RectToCornerPoints(RectF rect)
    {
        return
        [
            rect.Left, rect.Top,
            rect.Right, rect.Top,
            rect.Right, rect.Bottom,
            rect.Left, rect.Bottom
        ];
    }
}