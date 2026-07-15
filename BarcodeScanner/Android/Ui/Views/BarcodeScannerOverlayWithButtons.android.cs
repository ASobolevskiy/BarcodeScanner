using System.Diagnostics.CodeAnalysis;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Util;
using Android.Views;
using BarcodeScanner.Models;

namespace BarcodeScanner.Ui.Views;

public class BarcodeScannerOverlayWithButtons : FrameLayout, IActiveScannerOverlay
{
    private const int BUTTON_SIZE = 56;
    private const int BUTTON_MARGIN = 60;
    
    private BarcodeScannerOverlayView _drawingView;
    private Button? _btnBack;
    private Button? _btnTorch;
    private int _buttonSizePx;

    private bool _isTorchOn;

    public event Action? OnBackRequested;
    public event Action<bool>? OnTorchToggle;
    
    #region Ctors
    protected BarcodeScannerOverlayWithButtons(
        IntPtr javaReference, 
        JniHandleOwnership transfer) : base(javaReference, transfer) => Init();

    public BarcodeScannerOverlayWithButtons(
        Context context, 
        IAttributeSet? attrs, 
        int defStyleAttr, 
        int defStyleRes) : base(context, attrs, defStyleAttr, defStyleRes) => Init();

    public BarcodeScannerOverlayWithButtons(
        Context context, 
        IAttributeSet? attrs, 
        int defStyleAttr) : base(context, attrs, defStyleAttr) => Init();

    public BarcodeScannerOverlayWithButtons(
        Context context, 
        IAttributeSet? attrs) : base(context, attrs) => Init();

    public BarcodeScannerOverlayWithButtons(Context context) : base(context) => Init();

    #endregion
    
    [MemberNotNull(nameof(_drawingView), nameof(_btnBack), nameof(_btnTorch))]
    private void Init()
    {
        var density = Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        _buttonSizePx = (int)(BUTTON_SIZE * density);
        
        _drawingView = new BarcodeScannerOverlayView(Context);
        AddView(_drawingView, new LayoutParams(
                                               ViewGroup.LayoutParams.MatchParent, 
                                               ViewGroup.LayoutParams.MatchParent));
        
        _btnBack = CreateCircleButton("❌");
        var backParams = new LayoutParams(_buttonSizePx, _buttonSizePx)
        {
            Gravity = GravityFlags.Bottom | GravityFlags.Start,
            BottomMargin = BUTTON_MARGIN,
            MarginStart = BUTTON_MARGIN
        };
        _btnBack.Click += OnBtnBackClick;
        AddView(_btnBack, backParams);
        
        _btnTorch = CreateCircleButton("🔦");
        var torchParams = new LayoutParams(_buttonSizePx, _buttonSizePx)
        {
            Gravity = GravityFlags.Bottom | GravityFlags.End,
            BottomMargin = BUTTON_MARGIN,
            MarginEnd = BUTTON_MARGIN
        };
        _btnTorch.Click += OnBtnTorchClick;
        AddView(_btnTorch, torchParams);
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        _btnBack.Click -= OnBtnBackClick;
        _btnTorch.Click -= OnBtnTorchClick;
    }

    private void OnBtnBackClick(object? s, EventArgs e)
    {
        OnBackRequested?.Invoke();
    }

    private void OnBtnTorchClick(object? s, EventArgs e)
    {
        _isTorchOn = !_isTorchOn;
        UpdateTorchButtonAppearance();
        OnTorchToggle?.Invoke(_isTorchOn);
    }

    private Button CreateCircleButton(string text)
    {
        return new Button(Context)
        {
            Text = text,
            TextSize = 24f,
            Gravity = GravityFlags.Center,
            Background = CreateCircleBackground()
        }.Apply(btn =>
        {
            btn.SetPadding(0, 0, 0, 0);
            btn.SetMinimumHeight(0);
            btn.SetMinimumWidth(0);
        });
    }
    
    private GradientDrawable CreateCircleBackground(string backgroundColorHex = "#80000000")
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Oval);
        drawable.SetColor(Android.Graphics.Color.ParseColor(backgroundColorHex));
        drawable.SetStroke(4, Android.Graphics.Color.White); 
    
        return drawable;
    }
    
    private void UpdateTorchButtonAppearance()
    {
        var colorHex = _isTorchOn ? "#CCFFFF00" : "#80000000";
        _btnTorch?.Background = CreateCircleBackground(colorHex);
    }

    #region Public API

    public void ClearOverlay()
    {
        _drawingView?.ClearOverlay();
    }

    public void UpdateOverlay(string? barcodeValue, float[] targetPoints)
    {
        _drawingView.UpdateOverlay(barcodeValue, targetPoints);
    }

    public ViewFinderRect GetViewfinderRect() => _drawingView.GetViewfinderRect();

    public void SyncRegionOfInterest(RoiRect roi) => _drawingView.SyncRegionOfInterest(roi);

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _btnBack?.Dispose();
            _btnBack = null;
            _btnTorch?.Dispose();
            _btnTorch = null;
            _drawingView.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class ViewExtensions
{
    public static T Apply<T>(this T view, Action<T> action) where T : View
    {
        action(view);
        return view;
    }
}