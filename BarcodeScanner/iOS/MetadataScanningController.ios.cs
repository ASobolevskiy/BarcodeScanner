using System.Collections.Concurrent;
using AVFoundation;
using BarcodeScanner.Enums;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;
using BarcodeScanner.Ui.Views;
using CoreFoundation;

namespace BarcodeScanner;

public class MetadataScanningController(
    string instanceId) : UIViewController
{
    private static readonly ConcurrentDictionary<string, WeakReference<MetadataScanningController>> ActiveInstances =
        new();

    private BarcodeScanningOptions _options;
    private CGRect _roiRect;
    private UIView _overlayView;
    private IActiveScannerOverlay? _activeOverlay;
    private BarcodeDetectionHandler? _detectionHandler;
    private AVCaptureSession? _session;
    private AVCaptureDevice? _cameraDevice;
    private AVCaptureMetadataOutput? _metadataOutput;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private MetadataOutputDelegate? _metadataOutputDelegate;
    
    private DispatchQueue? _metadataQueue;

    private bool _isFinishing;
    private int _delayBeforeClose;

    private volatile bool _isScanning = true;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View;
        if (view is null) return;
        
        view.BackgroundColor = UIColor.Black;
        ActiveInstances[instanceId] = new WeakReference<MetadataScanningController>(this);

        _options = MobileBarcodeScanner.GetOptions(instanceId);
        _detectionHandler = new BarcodeDetectionHandler(_options);
        ApplyOptions(_options);
        SetupOverlay(view, _options);
        SetupCamera(view, _options);
        
        DispatchQueue.MainQueue.DispatchAsync(() => _session?.StartRunning());
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        var view = View;
        if(view is null || _previewLayer is null) return;
        _previewLayer.Frame = view.Bounds;
        view.LayoutIfNeeded();
        var newRoi = GetRoiRect();
        if (_roiRect.Equals(newRoi)) 
            return;
        _roiRect = newRoi;
        UpdateRectOfInterest();
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _session?.StopRunning();
        _metadataOutput?.SetDelegate(null, null);
        _metadataOutputDelegate = null;
        _previewLayer?.RemoveFromSuperLayer();
        
        ActiveInstances.TryRemove(instanceId, out _);
        
        if (!_isFinishing) 
            MobileBarcodeScanner.DispatchCancel(instanceId);
    }

    public override bool ShouldAutorotate()
    {
        return false;
    }

    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations()
    {
        return UIInterfaceOrientationMask.Portrait;
    }

    public override UIInterfaceOrientation PreferredInterfaceOrientationForPresentation()
    {
        return UIInterfaceOrientation.Portrait;
    }

    private void ApplyOptions(BarcodeScanningOptions options)
    {
        _delayBeforeClose = options.DelayBeforeScannerClose;
    }

    private void SetupOverlay(UIView parentView, BarcodeScanningOptions options)
    {
        UIView overlay;
        if (options.CustomOverlayFactory != null)
        {
            var overlayInstance = options.CustomOverlayFactory(this);
            if(overlayInstance is not UIView view)
                throw new InvalidOperationException("CustomOverlayFactory must return UIKit.UIView");
            
            overlay = view;
            if(overlayInstance is IActiveScannerOverlay activeOverlay)
                _activeOverlay = activeOverlay;
        }
        else
        {
            var defaultOverlay = new BarcodeScannerOverlayWithButtons();
            defaultOverlay.OnBackRequested += () =>
            {
                _isFinishing = true;
                MobileBarcodeScanner.DispatchCancel(instanceId);
                DismissViewController(true, null);
            };
            defaultOverlay.OnTorchToggle += isOn => SetTorchState(instanceId, isOn);

            overlay = defaultOverlay;
            _activeOverlay = defaultOverlay;
        }
        
        _overlayView = overlay;
        _overlayView.TranslatesAutoresizingMaskIntoConstraints = false;
        View?.Add(_overlayView);
        
        _overlayView.TopAnchor.ConstraintEqualTo(parentView.TopAnchor).Active = true;
        _overlayView.BottomAnchor.ConstraintEqualTo(parentView.BottomAnchor).Active = true;
        _overlayView.LeadingAnchor.ConstraintEqualTo(parentView.LeadingAnchor).Active = true;
        _overlayView.TrailingAnchor.ConstraintEqualTo(parentView.TrailingAnchor).Active = true;
        
        if(options.RegionOfInterest is {IsValid: true} roi)
            _activeOverlay?.SyncRegionOfInterest(roi);
        
#if DEBUG
        if (options is { RegionOfInterest.IsValid: true, CustomOverlayFactory: not null })
        {
            Console.WriteLine("[BarcodeScanner] [WARN] RegionOfInterest is set together with custom overlay. The drawn viewfinder may not" +
                              "match the actual scanning area unless the overlay implements IActiveScannerOverlay.SyncRegionOfInterest.");
        }
#endif
    }

    private void SetupCamera(UIView parentView, BarcodeScanningOptions options)
    {
        _session = new AVCaptureSession { SessionPreset = AVCaptureSession.PresetHigh };
        _cameraDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (_cameraDevice is null)
        {
            MobileBarcodeScanner.DispatchError(instanceId, "No camera device found");
            DismissViewController(true, null);
            return;
        }

        var input = new AVCaptureDeviceInput(_cameraDevice, out var error);
        if (error is not null || !_session.CanAddInput(input))
        {
            var errorMessage = error?.LocalizedDescription ?? "Unknown error";
            MobileBarcodeScanner.DispatchError(instanceId, errorMessage);
            DismissViewController(true, null);
            return;
        }
        _session.AddInput(input);

        _metadataOutput = new AVCaptureMetadataOutput();
        if (!_session.CanAddOutput(_metadataOutput))
        {
            MobileBarcodeScanner.DispatchError(instanceId, "Cannot add metadata output");
            DismissViewController(true, null);
            return;
        }
        _session.AddOutput(_metadataOutput);
        if (_metadataOutput.Connections is { Length: > 0 } && _metadataOutput.Connections[0] is { SupportsVideoOrientation: true} connection)
            connection.VideoOrientation = AVCaptureVideoOrientation.Portrait;

        var formats = options.PossibleFormats
                             .Select(f => f.ToMetadataType())
                             .Distinct()
                             .ToArray();
        _metadataOutput.MetadataObjectTypes = formats.ToBitmask();
        
        _metadataQueue = new DispatchQueue("metadataQueue");

        _metadataOutputDelegate = new MetadataOutputDelegate(HandleDetectedCodes);
        _metadataOutput.SetDelegate(_metadataOutputDelegate, _metadataQueue);
        
        _previewLayer = new AVCaptureVideoPreviewLayer(_session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = parentView.Bounds
        };

        if(_previewLayer.Connection is {SupportsVideoOrientation: true} previewConnection)
            previewConnection.VideoOrientation = AVCaptureVideoOrientation.Portrait;

        parentView.Layer.InsertSublayer(_previewLayer, 0);
        
        UpdateRectOfInterest();
        
        //_session.StartRunning();
    }

    private void HandleDetectedCodes(AVMetadataObject[]? metadataObjects)
    {
        if(!_isScanning || _detectionHandler is null)
            return;

        var barcodeDataList = new List<BarcodeData>();
        if (metadataObjects is { Length: > 0 })
        {
            foreach (var metadataObject in metadataObjects)
            {
                if (metadataObject is AVMetadataMachineReadableCodeObject codeObject &&
                    !string.IsNullOrWhiteSpace(codeObject.StringValue))
                {
                    var transformed = _previewLayer?.GetTransformedMetadataObject(codeObject) as AVMetadataMachineReadableCodeObject;
                    var floatPoints = transformed?.Corners.ToFloatArray();
                    if (floatPoints is { Length: 8 })
                    {
                        barcodeDataList.Add(new BarcodeData(
                                                            codeObject.StringValue,
                                                            codeObject.StringValue,
                                                            codeObject.Type.ToLocalFormat(),
                                                            floatPoints));
                    }
                }
            }
        }
        
        var roi = new RoiBounds((float)_roiRect.Left, (float)_roiRect.Top, (float)_roiRect.Right, (float)_roiRect.Bottom);
        var result = _detectionHandler.Process(barcodeDataList, roi);
        
        if (result is null) return;
        var value = result.Value;

        HandleResult(value);
    }

    private void HandleResult(DetectionResult result)
    {
        if (result.ShouldResetOverlay)
        {
            DispatchQueue.MainQueue.DispatchAsync(() => _activeOverlay?.ClearOverlay());
            return;
        }
        
        if (result.ScanType == ScanType.Continuous)
        {
            HandleBarcodeFoundInContinuousMode(result);
        }
        else
        {
            HandleBarcodeFoundInOneShotMode(result);
        }
        
        if (result.SmoothedPoints is not null)
        {
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                _activeOverlay?.UpdateOverlay(result.RawValue, result.SmoothedPoints);
            });
        }
    }

    private CGRect GetRoiRect()
    {
        
        var view = View;
        if (view is null) 
            return CGRect.Empty;
        if(_options.RegionOfInterest.HasValue)
            return _options.RegionOfInterest.Value.ToCgRect(view.Bounds.Width, view.Bounds.Height);

        return _activeOverlay is not null
            ? _activeOverlay.GetViewfinderRect().ToCgRect()
            : view.Bounds;
    }

    private void UpdateRectOfInterest()
    {
        if (_previewLayer is null || _metadataOutput is null)
            return;

        _metadataOutput.RectOfInterest = _previewLayer.MapToMetadataOutputCoordinates(_roiRect);
    }

    private void HandleBarcodeFoundInContinuousMode(DetectionResult detectionResult)
    {
        var result = new BarcodeResult
        {
            Status = ScanStatus.Success,
            Symbology = detectionResult.Symbology,
            RawValue = detectionResult.RawValue,
            DisplayValue = detectionResult.DisplayValue,
            ScannedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        
        MobileBarcodeScanner.DispatchContinuousResult(instanceId, result);
        
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, 
                                                               TimeSpan.FromMilliseconds(_delayBeforeClose)), 
                                              () => _activeOverlay?.ClearOverlay());
    }

    private void HandleBarcodeFoundInOneShotMode(DetectionResult detectionResult)
    {
        if (_isFinishing)
            return;
        
        _isScanning = false;
        _isFinishing = true;
        
        var result = new BarcodeResult
        {
            Status = ScanStatus.Success,
            Symbology = detectionResult.Symbology,
            RawValue = detectionResult.RawValue,
            DisplayValue = detectionResult.DisplayValue,
            ScannedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        
        MobileBarcodeScanner.DispatchSingleResult(instanceId, result);
        
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, 
                                                               TimeSpan.FromMilliseconds(_delayBeforeClose)), 
                                              () => DismissViewController(true, null));
    }
    
    private void SetTorchInternal(bool turnOn)
    {
        if (_cameraDevice is not { HasTorch: true }) return;

        _cameraDevice.LockForConfiguration(out var error);
        if (error == null)
        {
            _cameraDevice.TorchMode = turnOn ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off;
            _cameraDevice.UnlockForConfiguration();
        }
    }

    internal static void SetTorchState(string instanceId, bool isOn)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        if (ActiveInstances.TryGetValue(instanceId, out var weakRef) 
            && weakRef.TryGetTarget(out var controller))
        {
            controller.SetTorchInternal(isOn);
        }
    }

    internal static void FinishByInstanceId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        if (ActiveInstances.TryGetValue(instanceId, out var weakRef) 
            && weakRef.TryGetTarget(out var controller))
        {
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                if (!controller.IsBeingDismissed) 
                    controller.DismissViewController(true, null);
            });
        }
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