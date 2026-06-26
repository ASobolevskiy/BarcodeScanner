using System;
using System.Collections.Concurrent;
using System.Linq;
using AVFoundation;
using BarcodeScanner.Enums;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;
using BarcodeScanner.Ui.Views;
using CoreFoundation;
using Foundation;
using UIKit;

namespace BarcodeScanner;

public class MetadataScanningController(
    string instanceId) : UIViewController
{
    private static readonly ConcurrentDictionary<string, WeakReference<MetadataScanningController>> ActiveInstances =
        new();

    private UIView _overlayView;
    private IActiveScannerOverlay? _activeOverlay;
    private BarcodeDetectionHandler? _detectionHandler;
    private AVCaptureSession? _session;
    private AVCaptureDevice? _cameraDevice;
    private AVCaptureMetadataOutput? _metadataOutput;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    
    private DispatchQueue? _metadataQueue;

    private bool _isFinishing;
    private bool _isContinuousScan;
    private int _delayBeforeClose;

    private volatile bool _isScanning = true;
    
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View;
        if (view is null) return;
        
        view.BackgroundColor = UIColor.Black;
        ActiveInstances[instanceId] = new WeakReference<MetadataScanningController>(this);

        var options = MobileBarcodeScanner.GetOptions(instanceId);
        _detectionHandler = new BarcodeDetectionHandler(options);
        ApplyOptions(options);
        SetupOverlay(view, options);
        SetupCamera(view, options);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        var view = View;
        if(view is null || _previewLayer is null) return;
        _previewLayer.Frame = view.Bounds;
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _session?.StopRunning();
        _metadataOutput?.SetDelegate(null, null);
        _previewLayer?.RemoveFromSuperLayer();
        
        ActiveInstances.TryRemove(instanceId, out _);
        
        if (!_isFinishing) 
            MobileBarcodeScanner.DispatchCancel(instanceId);
    }

    private void ApplyOptions(BarcodeScanningOptions options)
    {
        _isContinuousScan = options.ScannerMode == ScanType.Continuous;
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

        var formats = options.PossibleFormats
                             .Select(f => f.ToMetadataType())
                             .Distinct()
                             .ToArray();
        _metadataOutput.MetadataObjectTypes = formats.ToBitmask();
        
        _metadataQueue = new DispatchQueue("metadataQueue");
        _metadataOutput.SetDelegate(new MetadataOutputDelegate(HandleDetectedCodes), _metadataQueue);
        
        _previewLayer = new AVCaptureVideoPreviewLayer(_session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = parentView.Bounds
        };
        
        parentView.Layer.InsertSublayer(_previewLayer, 0);
        
        _session.StartRunning();
    }

    private void HandleDetectedCodes(AVMetadataObject[]? metadataObjects)
    {
        if(!_isScanning || _detectionHandler is null)
            return;

        var currentTimeMs = (long)(NSDate.Now.SecondsSinceReferenceDate * 1000.0);
        var result = _detectionHandler.Process(metadataObjects, currentTimeMs, _previewLayer);

        if (result is null) return;
        var value = result.Value;

        if (value.ShouldResetOverlay)
        {
            DispatchQueue.MainQueue.DispatchAsync(() => _activeOverlay?.ClearOverlay());
            return;
        }

        if (_isContinuousScan)
        {
            HandleBarcodeFoundInContinuousMode(value);
        }
        else
        {
            HandleBarcodeFoundInOneShotMode(value);
        }

        if (value.SmoothedPoints is not null)
        {
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                _activeOverlay?.UpdateOverlay(value.RawValue, value.SmoothedPoints);
            });
        }
    }

    private void HandleBarcodeFoundInContinuousMode(DetectionResult detectionResult)
    {
        var result = new BarcodeResult
        {
            Status = ScanStatus.Success,
            Symbology = detectionResult.Symbology,
            RawValue = detectionResult.RawValue,
            DisplayValue = detectionResult.RawValue,
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
            DisplayValue = detectionResult.RawValue,
            ScannedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        
        MobileBarcodeScanner.DispatchSingleResult(instanceId, result);
        
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, 
                                                               TimeSpan.FromMilliseconds(_delayBeforeClose)), 
                                              () => DismissViewController(true, null));
    }
    
    private void SetTorchInternal(bool turnOn)
    {
        if (_cameraDevice == null || !_cameraDevice.HasTorch) return;

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