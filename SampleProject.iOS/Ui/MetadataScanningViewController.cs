using AVFoundation;
using CoreFoundation;
using SampleProject.iOS.Extensions;

namespace SampleProject.iOS;

public class MetadataScanningController : UIViewController
{
    private AVCaptureSession? _session;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private ScannerOverlayView? _overlayView;
    private AVCaptureMetadataOutput? _metadataOutput;
    private DispatchQueue? _metadataQueue;
    private volatile bool _isScanning = true;

    private readonly AVMetadataObjectType[] _barcodeTypes =
    [
        AVMetadataObjectType.QRCode,
        AVMetadataObjectType.DataMatrixCode,
        AVMetadataObjectType.Code128Code,
        AVMetadataObjectType.EAN13Code,
        AVMetadataObjectType.EAN8Code,
        AVMetadataObjectType.UPCECode,
        AVMetadataObjectType.Code39Code,
        AVMetadataObjectType.PDF417Code,
        AVMetadataObjectType.AztecCode,
    ];
    
    public Action<string, string>? OnBarcodeFound;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        if(View == null)
            return;
        View.BackgroundColor = UIColor.Black;

        SetupOverlay();
        SetupCamera();
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        _previewLayer?.Frame = View?.Bounds ?? CGRect.Empty;
    }
    
    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _session?.StopRunning();
        _metadataOutput?.SetDelegate(null, null);
        _previewLayer?.RemoveFromSuperLayer();
    }

    private void SetupOverlay()
    {
        if(View == null)
            return;
        _overlayView = new ScannerOverlayView();
        _overlayView.TranslatesAutoresizingMaskIntoConstraints = false;
        Add(_overlayView);
        
        _overlayView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor).Active = true;
        _overlayView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor).Active = true;
        _overlayView.TopAnchor.ConstraintEqualTo(View.TopAnchor).Active = true;
        _overlayView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor).Active = true;
    }

    private void SetupCamera()
    {
        _session = new AVCaptureSession
        {
            SessionPreset = AVCaptureSession.PresetHigh
        };
        
        var cameraDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (cameraDevice == null)
        {
            ShowAlert("Камера недоступна"); 
            return;
        }
        
        var input = new AVCaptureDeviceInput(cameraDevice, out var error);
        if (error != null)
        {
            ShowAlert($"Ошибка камеры: {error.LocalizedDescription}");
            return;
        }
        
        if (_session.CanAddInput(input))
            _session.AddInput(input);
        
        _metadataOutput = new AVCaptureMetadataOutput();
        if (_session.CanAddOutput(_metadataOutput))
            _session.AddOutput(_metadataOutput);
        _metadataOutput.MetadataObjectTypes = _barcodeTypes.ToBitmask();
        
        _metadataQueue = new DispatchQueue("metadataQueue");
        _metadataOutput.SetDelegate(new MetadataOutputDelegate(HandleDetectedCodes), _metadataQueue);

        _previewLayer = new AVCaptureVideoPreviewLayer(_session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = View?.Bounds ?? CGRect.Empty
        };
        
        View?.Layer.InsertSublayer(_previewLayer, 0);
        
        _session.StartRunning();
    }

    private void HandleDetectedCodes(AVMetadataObject[]? metadataObjects)
    {
        if (!_isScanning || metadataObjects == null)
            return;
        var codes = metadataObjects.Cast<AVMetadataMachineReadableCodeObject>().ToArray();
        if (codes.Length == 0)
        {
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                _overlayView?.ResetToViewfinder();
            });
        }

        var firstCode = codes[0];
        if (string.IsNullOrEmpty(firstCode.StringValue))
            return;

        _isScanning = false;
        var transformed = _previewLayer?.GetTransformedMetadataObject(firstCode) as AVMetadataMachineReadableCodeObject;
        var rect = GetBoundingRectFromCorners(transformed?.Corners);
        
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            _overlayView?.UpdateCorners(rect, animate: true, isDetected: true);
            OnBarcodeFound?.Invoke(firstCode.Type.ToString(), firstCode.StringValue);
        });

        DispatchQueue.MainQueue.DispatchAfter(
                                              new DispatchTime(DispatchTime.Now, TimeSpan.FromMilliseconds(500)),
                                              () =>
                                              {
                                                  DismissViewController(true, null);
                                              });
    }

    private CGRect GetBoundingRectFromCorners(CGPoint[]? corners)
    {
        if (corners == null || corners.Length < 4) 
            return CGRect.Empty;

        float minHeight = 50;
        
        var x = corners.Min(p => p.X);
        var y = corners.Min(p => p.Y);
        var w = corners.Max(p => p.X) - x;
        var h = corners.Max(p => p.Y) - y;

        var finalH = Math.Max(h, minHeight);
        
        return new CGRect(x, y, w, finalH);
    }
    
    private void ShowAlert(string message)
    {
        var alert = UIAlertController.Create("Ошибка", message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }

    private class MetadataOutputDelegate(Action<AVMetadataObject[]>? callback) : AVCaptureMetadataOutputObjectsDelegate
    {
        public override void DidOutputMetadataObjects(AVCaptureMetadataOutput captureOutput, AVMetadataObject[] metadataObjects,
            AVCaptureConnection connection)
        {
            callback?.Invoke(metadataObjects);
        }
    }
}