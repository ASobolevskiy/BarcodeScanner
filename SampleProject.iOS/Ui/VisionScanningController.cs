using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using ImageIO;
using Vision;

namespace SampleProject.iOS;

public class VisionScanningViewController : UIViewController
{
    private ScannerOverlayView? _overlayView;
    private AVCaptureSession? _captureSession;
    private AVCaptureVideoPreviewLayer? _previewLayer;

    private readonly VNBarcodeSymbology[] _symbologies =
    [
        VNBarcodeSymbology.QR,
        VNBarcodeSymbology.DataMatrix,
        VNBarcodeSymbology.Code128,
        VNBarcodeSymbology.Ean13,
        VNBarcodeSymbology.Ean8,
        VNBarcodeSymbology.Upce,
        VNBarcodeSymbology.Code39,
        VNBarcodeSymbology.Pdf417,
        VNBarcodeSymbology.Aztec
    ];

    private VNRequestCompletionHandler? _completionHandler;
    public Action<string, string>? OnBarcodeFound;
    
    private CameraDelegate? _cameraDelegate;
    private DispatchQueue? _cameraQueue;

    private int _isFinishing;
    private int _videoWidth;
    private int _videoHeight;
    private volatile bool _videoDimensionsReady;
    private AVCaptureVideoDataOutput? _videoOutput;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        if(View == null)
            return;
        View.BackgroundColor = UIColor.White;
        SetupBarcodeDetection();
        SetupCamera();

        _overlayView = new ScannerOverlayView();
        _overlayView.TranslatesAutoresizingMaskIntoConstraints = false;
        Add(_overlayView);
        
        _overlayView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor).Active = true;
        _overlayView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor).Active = true;
        _overlayView.TopAnchor.ConstraintEqualTo(View.TopAnchor).Active = true;
        _overlayView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor).Active = true;
    }

    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        Interlocked.Exchange(ref _isFinishing, 0);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        _previewLayer?.Frame = View?.Bounds ?? CGRect.Empty;
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _captureSession?.StopRunning();
    
        // 🔥 Очистка ресурсов
        _videoOutput?.SetSampleBufferDelegate(null, null);

        _cameraDelegate = null;
        _cameraQueue = null;
        _previewLayer?.RemoveFromSuperLayer();
        _previewLayer = null;
    }

    private void SetupBarcodeDetection()
    {
        Console.WriteLine("SetupBarcodeDetection");
        _completionHandler = (request, error) =>
        {
            if (error != null)
                return;
            var observations = request.GetResults<VNBarcodeObservation>();
            if (observations == null || observations.Length == 0)
                return;
            var firstBarcode = observations[0];
            if (firstBarcode == null || string.IsNullOrEmpty(firstBarcode.PayloadStringValue))
                return;

            if (Interlocked.CompareExchange(ref _isFinishing, 1, 0) == 1)
                return;
            
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                var screenRect = ConvertVisionRectToScreenRect(firstBarcode.BoundingBox);
                _overlayView?.UpdateCorners(screenRect, animate: true, isDetected: true);
            });
            
            var formatName = firstBarcode.Symbology.ToString();
            DispatchQueue.MainQueue.DispatchAsync(() => 
                                                      OnBarcodeFound?.Invoke(formatName, firstBarcode.PayloadStringValue));

            DispatchQueue.MainQueue.DispatchAfter(
                                                  new DispatchTime(DispatchTime.Now, TimeSpan.FromMilliseconds(500)),
                                                  () => InvokeOnMainThread(() => DismissViewController(true, null)));
        };
    }

    private void SetupCamera()
    {
        _captureSession = new AVCaptureSession();
        _captureSession.SessionPreset = AVCaptureSession.PresetHigh;
        
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
        
        if (_captureSession.CanAddInput(input))
            _captureSession.AddInput(input);

        _videoOutput = new AVCaptureVideoDataOutput();
        _videoOutput.AlwaysDiscardsLateVideoFrames = true;
        _cameraDelegate = new CameraDelegate(_symbologies, _completionHandler);
        _cameraDelegate.OnVideoDimensionsReady = (width, height) =>
        {
            _videoWidth = width;
            _videoHeight = height;
            _videoDimensionsReady = true;
            Console.WriteLine($"[DBG] Video dimensions: {width}x{height}");
        };
        
        _cameraQueue = new DispatchQueue("cameraQueue"); 
        _videoOutput.SetSampleBufferDelegate(_cameraDelegate, _cameraQueue);
        if(_captureSession.CanAddOutput(_videoOutput))
        {
            Console.WriteLine("Setting up output");
            _captureSession.AddOutput(_videoOutput);
        }

        if (_videoOutput?.Connections.Length != 0 && _videoOutput?.Connections[0] is { } connection)
        {
            SetConnectionOrientation(connection);
        }

        _previewLayer = new AVCaptureVideoPreviewLayer(_captureSession);
        _previewLayer.Frame = View?.Bounds ?? CGRect.Empty;
        _previewLayer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;
        View?.Layer.AddSublayer(_previewLayer);
        
        _captureSession.StartRunning();
    }
    
    private void ShowAlert(string message)
    {
        var alert = UIAlertController.Create("Ошибка", message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }

    private CGRect ConvertVisionRectToScreenRect(CGRect visionRect)
    {
        var minHeight = 50f;
        if (_previewLayer == null || View == null || !_videoDimensionsReady)
            return CGRect.Empty;

        var visionBottomLeft = new CGPoint(visionRect.GetMinX(), visionRect.GetMinY());
        var visionTopRight = new CGPoint(visionRect.GetMaxX(), visionRect.GetMaxY());
        
        var mappedBottomLeft = MapVisionPointToScreen(visionBottomLeft);
        var mappedTopRight = MapVisionPointToScreen(visionTopRight);
        
        if (mappedBottomLeft.IsEmpty || mappedTopRight.IsEmpty)
            return CGRect.Empty;
        
        var x = Math.Min(mappedBottomLeft.X, mappedTopRight.X);
        var y = Math.Min(mappedBottomLeft.Y, mappedTopRight.Y);
        var width = Math.Abs(mappedBottomLeft.X - mappedTopRight.X);
        var height = Math.Abs(mappedBottomLeft.Y - mappedTopRight.Y);
        
        var finalHeight = Math.Max(height, minHeight);
    
        return new CGRect(x, y, width, finalHeight);
    }

    private CGPoint MapVisionPointToScreen(CGPoint visionPoint)
    {
        var cameraPoint = new CGPoint(visionPoint.X, 1.0 - visionPoint.Y);
        var screenPoint = _previewLayer.PointForCaptureDevicePointOfInterest(cameraPoint);
        if(double.IsNaN(screenPoint.X) || double.IsNaN(screenPoint.Y) || double.IsInfinity(screenPoint.X) || double.IsInfinity(screenPoint.Y))
            return CGPoint.Empty;
        return screenPoint;
    }

    private CGRect GetVisibleRect(float videoRatio, float layerRatio, CGRect layerBounds)
    {
        CGRect visibleRect;
        if (videoRatio > layerRatio)
        {
            var scale = (float)layerBounds.Height / _videoHeight;
            Console.WriteLine($"[DBG] Scale: {scale}");
            var visibleWidth = _videoWidth * scale;
            Console.WriteLine($"[DBG] Visible width: {visibleWidth}");
            visibleRect = new CGRect(
                                     (layerBounds.Width - visibleWidth) / 2f,
                                     0,
                                     visibleWidth,
                                     (float)layerBounds.Height);
        }
        else
        {
            var scale = (float)layerBounds.Width / _videoWidth;
            Console.WriteLine($"[DBG] Scale: {scale}");
            var visibleHeight = _videoHeight * scale;
            Console.WriteLine($"[DBG] Visible height: {visibleHeight}");
            visibleRect = new CGRect(
                                     0,
                                     (layerBounds.Height - visibleHeight) / 2f,
                                     (float)layerBounds.Width,
                                     visibleHeight);
        }

        return visibleRect;
    }

    private void SetConnectionOrientation(AVCaptureConnection connection)
    {
        var device = UIDevice.CurrentDevice;
        if (device.CheckSystemVersion(17, 0))
        {
            connection.VideoRotationAngle = device.Orientation switch
            {
                UIDeviceOrientation.Portrait => 0,
                UIDeviceOrientation.LandscapeRight => (nfloat)(Math.PI / 2),   // 90°
                UIDeviceOrientation.PortraitUpsideDown => (nfloat)Math.PI,     // 180°
                UIDeviceOrientation.LandscapeLeft => (nfloat)(-Math.PI / 2),   // -90°
                _ => 0
            };
        }
        else
        {
            if (connection.SupportsVideoOrientation)
            {
                connection.VideoOrientation = GetVideoOrientationFromDevice();
            }
        }
    }
    
    private AVCaptureVideoOrientation GetVideoOrientationFromDevice()
    {
        return UIDevice.CurrentDevice.Orientation switch
        {
            UIDeviceOrientation.Portrait => AVCaptureVideoOrientation.Portrait,
            UIDeviceOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
            UIDeviceOrientation.LandscapeLeft => AVCaptureVideoOrientation.LandscapeLeft,
            UIDeviceOrientation.LandscapeRight => AVCaptureVideoOrientation.LandscapeRight,
            _ => AVCaptureVideoOrientation.Portrait
        };
    }

    private class CameraDelegate(
        VNBarcodeSymbology[] symbologies, 
        VNRequestCompletionHandler? completionHandler) : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private volatile int _isProcessing;
        
        public Action<int, int>? OnVideoDimensionsReady;
        private volatile int _dimensionsSent;

        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, 
                                                   CMSampleBuffer sampleBuffer, 
                                                   AVCaptureConnection connection)
        {
            if (_dimensionsSent == 0)
            {
                var dims = sampleBuffer.GetVideoFormatDescription()?.Dimensions;
                if (dims != null && Interlocked.CompareExchange(ref _dimensionsSent, 1, 0) == 0)
                {
                    OnVideoDimensionsReady?.Invoke(dims.Value.Width, dims.Value.Height);
                }
            }
            
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 1)
            {
                sampleBuffer.Dispose();
                return;
            }

            var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
            sampleBuffer.Dispose();

            if (pixelBuffer == null)
            {
                Interlocked.Exchange(ref _isProcessing, 0);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var request = new VNDetectBarcodesRequest(completionHandler);
                    request.Symbologies = symbologies;
                    
                    var orientation = connection?.VideoOrientation ?? AVCaptureVideoOrientation.Portrait;
                    var cgOrientation = orientation switch
                    {
                        AVCaptureVideoOrientation.Portrait => CGImagePropertyOrientation.Right,
                        AVCaptureVideoOrientation.LandscapeLeft => CGImagePropertyOrientation.Up,
                        AVCaptureVideoOrientation.LandscapeRight => CGImagePropertyOrientation.Down,
                        AVCaptureVideoOrientation.PortraitUpsideDown => CGImagePropertyOrientation.Left,
                        _ => CGImagePropertyOrientation.Up
                    };
                    
                    using var handler = new VNImageRequestHandler(pixelBuffer, cgOrientation, []);
                    handler.Perform([request], out _);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[DBG] 💥 Exception: {e.Message}");
                }
                finally
                {
                    pixelBuffer.Dispose();
                    Interlocked.Exchange(ref _isProcessing, 0);
                }
            });
        }
    }
}