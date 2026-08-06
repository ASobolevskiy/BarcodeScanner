using System;
using BarcodeScanner.Models;
using UIKit;

namespace BarcodeScanner.Ui.Views;

internal sealed class BarcodeScannerOverlayWithButtons : UIView, IActiveScannerOverlay
{
    private const int BUTTON_SIZE = 56;
    private const int BUTTON_MARGIN = 30;
    private const float BUTTON_FONT_SIZE = 24f;
    
    private readonly BarcodeScannerOverlayView _drawingView;
    private readonly UIButton _btnBack;
    private readonly UIButton _btnTorch;
    
    private bool _isTorchOn;
    
    public event Action? OnBackRequested;
    public event Action<bool>? OnTorchToggle;

    public BarcodeScannerOverlayWithButtons()
    {
        BackgroundColor = UIColor.Clear;
        
        _drawingView = new BarcodeScannerOverlayView
        {
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        AddSubview(_drawingView);
        _drawingView.TopAnchor.ConstraintEqualTo(TopAnchor).Active = true;
        _drawingView.BottomAnchor.ConstraintEqualTo(BottomAnchor).Active = true;
        _drawingView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor).Active = true;
        _drawingView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor).Active = true;

        // Buttons are native subviews (AddSubview retains them), so their managed wrappers are
        // unconditional GC roots for as long as they're on screen. A TouchUpInside closure
        // capturing `this` directly would make this overlay reachable through that root forever
        // — the .NET-for-iOS runtime can never collect a cycle crossing a natively-retained
        // object. Capturing only a WeakReference avoids creating that edge.
        var weakSelf = new WeakReference<BarcodeScannerOverlayWithButtons>(this);

        _btnBack = CreateCircleButton("❌");
        AddSubview(_btnBack);
        _btnBack.BottomAnchor.ConstraintEqualTo(BottomAnchor, -BUTTON_MARGIN).Active = true;
        _btnBack.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, BUTTON_MARGIN).Active = true;
        _btnBack.TouchUpInside += (_, _) =>
        {
            if (weakSelf.TryGetTarget(out var self))
                self.OnBackRequested?.Invoke();
        };

        _btnTorch = CreateCircleButton("🔦");
        AddSubview(_btnTorch);
        _btnTorch.BottomAnchor.ConstraintEqualTo(BottomAnchor, -BUTTON_MARGIN).Active = true;
        _btnTorch.RightAnchor.ConstraintEqualTo(RightAnchor, -BUTTON_MARGIN).Active = true;
        _btnTorch.TouchUpInside += (_, _) =>
        {
            if (weakSelf.TryGetTarget(out var self))
                self.HandleTorchButtonTapped();
        };
    }

    private void HandleTorchButtonTapped()
    {
        _isTorchOn = !_isTorchOn;
        UpdateTorchButtonAppearance();
        OnTorchToggle?.Invoke(_isTorchOn);
    }
    
    private void UpdateTorchButtonAppearance()
    {
        var color = _isTorchOn 
            ? UIColor.FromRGBA(1f, 1f, 0f, 0.8f) 
            : UIColor.FromWhiteAlpha(0f, 0.5f);
        _btnTorch.Layer.BackgroundColor = color.CGColor;
    }
    
    private UIButton CreateCircleButton(string text)
    {
        var btn = new UIButton(UIButtonType.Custom) { TranslatesAutoresizingMaskIntoConstraints = false };
        btn.SetTitle(text, UIControlState.Normal);
        btn.SetTitleColor(UIColor.White, UIControlState.Normal);
        btn.TitleLabel.Font = UIFont.SystemFontOfSize(BUTTON_FONT_SIZE);
        
        btn.WidthAnchor.ConstraintEqualTo(BUTTON_SIZE).Active = true;
        btn.HeightAnchor.ConstraintEqualTo(BUTTON_SIZE).Active = true;
        
        btn.Layer.BackgroundColor = UIColor.FromWhiteAlpha(0f, 0.5f).CGColor;
        btn.Layer.BorderColor = UIColor.White.CGColor;
        btn.Layer.BorderWidth = 2f;
        btn.Layer.CornerRadius = BUTTON_SIZE / 2; 
        
        return btn;
    }

    public void ClearOverlay() => _drawingView.ClearOverlay();

    public void UpdateOverlay(string? barcodeValue, float[] targetPoints) => 
        _drawingView.UpdateOverlay(barcodeValue, targetPoints);

    public ViewFinderRect GetViewfinderRect() => _drawingView.GetViewfinderRect();

    public void SyncRegionOfInterest(RoiRect roi) => _drawingView.SyncRegionOfInterest(roi);
}