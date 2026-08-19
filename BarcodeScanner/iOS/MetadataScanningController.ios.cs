using System.Diagnostics;
using System.Threading;
using AVFoundation;
using BarcodeScanner.Enums;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;
using BarcodeScanner.Ui.Views;
using CoreFoundation;

namespace BarcodeScanner;

internal sealed record RoiSnapshot(float Left, float Top, float Right, float Bottom);

internal class MetadataScanningController(
    string instanceId) : UIViewController, IScannerPlatform
{
    private BarcodeScanningOptions _options;
    private CGRect _roiRect;
    private volatile RoiSnapshot _publishedRoi = new(0, 0, 0, 0);
    private UIView _overlayView;
    private IActiveScannerOverlay? _activeOverlay;
    private BarcodeDetectionHandler? _detectionHandler;

    // Reused across HandleDetectedCodes calls instead of allocating a new List every frame
    // (IOS-04 in CONTEXT.md). Safe: HandleDetectedCodes always runs on the single serial
    // _metadataQueue below, and BarcodeDetectionHandler.Process only reads from this list
    // synchronously - it never retains a reference to it past the call.
    private readonly List<BarcodeData> _barcodeBuffer = [];

    // Cached once instead of allocating a fresh closure on every HandleResult call whose
    // ShouldResetOverlay branch fires - the by far most common case while scanning (no barcode
    // currently in the ROI). Unlike the sibling UpdateOverlay dispatch, this one needs no
    // per-call data, so a single reused delegate is safe.
    private Action _clearOverlayAction;

    private void ClearOverlayOnMain() => _activeOverlay?.ClearOverlay();

    private AVCaptureSession? _session;
    private AVCaptureDevice? _cameraDevice;
    private AVCaptureMetadataOutput? _metadataOutput;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private MetadataOutputDelegate? _metadataOutputDelegate;

    private DispatchQueue? _metadataQueue;

    // A SetTorch() call that arrives before SetupCamera assigns _cameraDevice is remembered here
    // instead of being silently dropped. SetTorch()/SetTorchInternal() can be invoked from any
    // thread - ToggleTorch() on the shared side has no main-thread requirement - while SetupCamera
    // always runs on the main thread as part of ViewDidLoad, so this uses Interlocked rather than
    // a plain field. bool? can't be volatile, hence the tri-state int.
    private const int NoPendingTorchRequest = 0;
    private const int PendingTorchOn = 1;
    private const int PendingTorchOff = 2;
    private int _pendingTorchState;

    // Single serial queue for the whole AVCaptureSession lifecycle (start + stop). Both
    // StartRunning() and StopRunning() are blocking calls that must never run concurrently with
    // each other on the same session (IOS-03 in CONTEXT.md) - a serial queue gives that guarantee
    // for free (one block at a time, in submission order), the same way the main-thread queue used
    // to (accidentally, at the cost of blocking UI) before this queue existed.
    private readonly DispatchQueue _sessionQueue = new("sessionQueue");

    private volatile bool _isFinishing;
    private bool _isDismissed;
    private bool _hasAppeared;
    private bool _dismissPending;
    private int _delayBeforeClose;

    // GCD has no built-in "cancel a DispatchAfter block" primitive here (no DispatchWorkItem
    // in this binding), so a stale scheduled reset is suppressed via a generation counter
    // instead: only the block scheduled by the most recent detection is allowed to fire.
    private int _continuousResetGeneration;

    private volatile bool _isScanning = true;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View;
        if (view is null)
        {
            MobileBarcodeScanner.DispatchError(instanceId, "View failed to load.");
            DismissOnce();
            return;
        }

        view.BackgroundColor = UIColor.Black;
        MobileBarcodeScanner.AttachPlatformSession(instanceId, new IosScannerSession(new WeakReference<IScannerPlatform>(this)));

        _options = MobileBarcodeScanner.GetOptions(instanceId);
        _detectionHandler = new BarcodeDetectionHandler(_options);
        _clearOverlayAction = ClearOverlayOnMain;
        ApplyOptions(_options);
        if (!SetupOverlay(view, _options))
            return;
        if (!SetupCamera(view, _options))
            return;

        _sessionQueue.DispatchAsync(() => _session?.StartRunning());
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
        _publishedRoi = new RoiSnapshot((float)newRoi.Left, (float)newRoi.Top, (float)newRoi.Right, (float)newRoi.Bottom);
        UpdateRectOfInterest();
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        _hasAppeared = true;

        if (_dismissPending)
        {
            _dismissPending = false;
            DismissOnce();
        }
    }

    public override void ViewDidDisappear(bool animated)
    {
        base.ViewDidDisappear(animated);

        // ViewWillDisappear/ViewDidDisappear also fire when something is merely presented on
        // top of this screen (e.g. a system permission alert), not only when this VC is actually
        // being dismissed. Tearing the capture session down in that case would kill scanning for
        // good and never restore the preview layer when the user returns.
        if (!IsBeingDismissed)
            return;

        if (_session is { } sessionToStop)
        {
            _sessionQueue.DispatchAsync(() =>
            {
                try
                {
                    sessionToStop.StopRunning();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
            });
        }

        try
        {
            _metadataOutput?.SetDelegate(null, null);
            _metadataOutputDelegate = null;
            _previewLayer?.RemoveFromSuperLayer();
            _overlayView?.RemoveFromSuperview();
        }
        finally
        {
            if (!_isFinishing) 
                MobileBarcodeScanner.DispatchCancel(instanceId);
        }
    }

    public override bool ShouldAutorotate() => false;
    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations() => UIInterfaceOrientationMask.Portrait;
    public override UIInterfaceOrientation PreferredInterfaceOrientationForPresentation() => UIInterfaceOrientation.Portrait;

    private void ApplyOptions(BarcodeScanningOptions options)
    {
        _delayBeforeClose = options.DelayBeforeScannerClose;
    }

    private bool SetupOverlay(UIView parentView, BarcodeScanningOptions options)
    {
        // Guards the whole body because it calls into arbitrary consumer code
        // (CustomOverlayFactory) with no way to know what it might throw. SetupOverlay runs from
        // ViewDidLoad, which has no enclosing try/catch above it - confirmed on-device: an
        // unhandled exception here is silently swallowed somewhere below ViewDidLoad instead of
        // reaching ScanAsync's own catch, leaving the scanner screen never shown and the
        // caller's Task hanging forever. Mirrors Android's fix in commit 013e0d6 (CONC-03 in
        // CONTEXT.md).
        try
        {
            UIView overlay;
            if (options.CustomOverlayFactory != null)
            {
                var overlayInstance = options.CustomOverlayFactory(this);
                if(overlayInstance is not UIView view)
                {
                    MobileBarcodeScanner.DispatchError(instanceId, "CustomOverlayFactory must return UIKit.UIView");
                    DismissOnce();
                    return false;
                }

                overlay = view;
                if(overlayInstance is IActiveScannerOverlay activeOverlay)
                    _activeOverlay = activeOverlay;
            }
            else
            {
                var defaultOverlay = new BarcodeScannerOverlayWithButtons();

                // defaultOverlay is a native subview (View.subviews retains it), so its managed
                // wrapper is an unconditional GC root for as long as it's on screen. A closure
                // capturing `this` directly would make this controller reachable through that root
                // forever, and the .NET-for-iOS runtime can never collect a cycle that crosses a
                // natively-retained object. Capturing only a WeakReference avoids creating that edge.
                var weakSelf = new WeakReference<MetadataScanningController>(this);
                defaultOverlay.OnBackRequested += () =>
                {
                    if (weakSelf.TryGetTarget(out var self))
                        self.HandleOverlayBackRequested();
                };
                defaultOverlay.OnTorchToggle += turnOn =>
                {
                    if (weakSelf.TryGetTarget(out var self))
                        self.SetTorchInternal(turnOn);
                };

                overlay = defaultOverlay;
                _activeOverlay = defaultOverlay;
            }

            // UIView.Add (addSubview:) does not throw if the view already has a superview - it
            // silently detaches it from wherever it currently lives and reattaches it here. For a
            // consumer that violates the CustomOverlayFactory "return a fresh, unattached view"
            // contract, that means their own UI silently loses a child view with no diagnostic,
            // and - since the library only ever detaches the overlay on session end (never re-adds
            // it anywhere) - it never comes back. Reject this explicitly instead, matching the
            // crash guard Android needs for the same misuse (that platform's AddView throws).
            if (overlay.Superview is not null)
            {
                MobileBarcodeScanner.DispatchError(instanceId,
                    "CustomOverlayFactory must return a fresh, unattached UIView for every scan session.");
                DismissOnce();
                return false;
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

            return true;
        }
        catch (Exception ex)
        {
            // Deliberately no extra cleanup here: _overlayView is only ever read inside this
            // same method (verified across the file), so returning early leaves nothing
            // dangling for ViewDidDisappear/DismissOnce to trip over.
            Debug.WriteLine($"[BarcodeScanner] SetupOverlay failed: {ex.Message}");
            Debug.WriteLine($"[BarcodeScanner] StackTrace: {ex.StackTrace}");
            MobileBarcodeScanner.DispatchError(instanceId, $"Failed to set up scanner overlay: {ex.Message}");
            DismissOnce();
            return false;
        }
    }

    private bool SetupCamera(UIView parentView, BarcodeScanningOptions options)
    {
        // Guards the whole body for the same reason as SetupOverlay: called from ViewDidLoad,
        // which has no enclosing try/catch above it, and this method reaches into
        // AVFoundation/native APIs plus caller-supplied options (PossibleFormats) that can throw
        // (API-11 in CONTEXT.md) - confirmed on the simulator that an unhandled exception here is
        // silently swallowed the same way as SetupOverlay's, not a crash: the scanner screen
        // dismisses with no diagnostic and the caller's Task hangs forever.
        try
        {
            _session = new AVCaptureSession { SessionPreset = AVCaptureSession.PresetHigh };
            _cameraDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (_cameraDevice is null)
            {
                MobileBarcodeScanner.DispatchError(instanceId, "No camera device found");
                DismissOnce();
                return false;
            }

            var pendingTorch = Interlocked.Exchange(ref _pendingTorchState, NoPendingTorchRequest);
            if (pendingTorch != NoPendingTorchRequest)
                SetTorchInternal(pendingTorch == PendingTorchOn);

            var input = new AVCaptureDeviceInput(_cameraDevice, out var error);
            if (error is not null || !_session.CanAddInput(input))
            {
                var errorMessage = error?.LocalizedDescription ?? "Unknown error";
                MobileBarcodeScanner.DispatchError(instanceId, errorMessage);
                DismissOnce();
                return false;
            }
            _session.AddInput(input);

            _metadataOutput = new AVCaptureMetadataOutput();
            if (!_session.CanAddOutput(_metadataOutput))
            {
                MobileBarcodeScanner.DispatchError(instanceId, "Cannot add metadata output");
                DismissOnce();
                return false;
            }
            _session.AddOutput(_metadataOutput);
            if (_metadataOutput.Connections is { Length: > 0 } && _metadataOutput.Connections[0] is { SupportsVideoOrientation: true} connection)
                connection.VideoOrientation = AVCaptureVideoOrientation.Portrait;

            _metadataOutput.MetadataObjectTypes = ResolveMetadataObjectTypes(options.PossibleFormats, _metadataOutput);

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
            return true;
        }
        catch (Exception ex)
        {
            // Deliberately no extra cleanup here: a partially-built _session/_metadataOutput/
            // _previewLayer is safe to leave assigned - ViewDidDisappear/dispose already null-
            // check every one of them, and returning false here stops ViewDidLoad from ever
            // scheduling _session?.StartRunning() on it.
            Debug.WriteLine($"[BarcodeScanner] SetupCamera failed: {ex.Message}");
            Debug.WriteLine($"[BarcodeScanner] StackTrace: {ex.StackTrace}");
            MobileBarcodeScanner.DispatchError(instanceId, $"Failed to set up camera: {ex.Message}");
            DismissOnce();
            return false;
        }
    }

    /// <summary>
    /// Builds the metadata type mask strictly from known barcode symbologies.
    /// AVFoundation has no single "all barcode formats" constant like ML Kit does, and
    /// AVCaptureMetadataOutput.AvailableMetadataObjectTypes also includes non-barcode types
    /// (Face, and on newer iOS versions HumanBody/CatBody/DogBody/SalientObject) — using it
    /// directly would silently turn on face/body detection, which this library must never do.
    /// The intersection with AvailableMetadataObjectTypes only guards against requesting a
    /// barcode type the connected input doesn't support (AVFoundation throws otherwise);
    /// it never widens the result beyond actual barcode symbologies.
    /// </summary>
    private static AVMetadataObjectType ResolveMetadataObjectTypes(
        IEnumerable<BarcodeSymbology> possibleFormats,
        AVCaptureMetadataOutput metadataOutput)
    {
        var symbologies = possibleFormats as ICollection<BarcodeSymbology> ?? possibleFormats.ToArray();

        var effective = symbologies.Count == 0 || symbologies.Contains(BarcodeSymbology.AllSymbologies)
            ? BarcodeSymbologySet.AllConcrete
            : symbologies;

        var requestedBarcodeTypes = effective
                                    .Select(f => f.ToMetadataType())
                                    .Where(t => t != AVMetadataObjectType.None)
                                    .Distinct()
                                    .ToArray()
                                    .ToBitmask();

        return requestedBarcodeTypes & metadataOutput.AvailableMetadataObjectTypes;
    }

    private void HandleDetectedCodes(AVMetadataObject[]? metadataObjects)
    {
        if(!_isScanning || _detectionHandler is null)
            return;

        if (!_detectionHandler.ShouldProcessFrame())
            return;

        _barcodeBuffer.Clear();
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
                        _barcodeBuffer.Add(new BarcodeData(
                                                           codeObject.StringValue,
                                                           codeObject.StringValue,
                                                           codeObject.Type.ToLocalFormat(),
                                                           floatPoints));
                    }
                }
            }
        }

        var snapshot = _publishedRoi;
        var roi = new RoiBounds(snapshot.Left, snapshot.Top, snapshot.Right, snapshot.Bottom);
        var result = _detectionHandler.Process(_barcodeBuffer, roi);
        
        if (result is null) return;
        var value = result.Value;

        HandleResult(value);
    }

    private void HandleResult(DetectionResult result)
    {
        if (result.ShouldResetOverlay)
        {
            DispatchQueue.MainQueue.DispatchAsync(_clearOverlayAction);
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

        if (_options.RegionOfInterest is { } roi)
        {
            if (roi.IsValid)
                return roi.ToCgRect(view.Bounds.Width, view.Bounds.Height);

            Debug.WriteLine("[BarcodeScanner] Warning: RegionOfInterest is set but invalid " +
                             "(coordinates must be in [0,1] with Right > Left and Bottom > Top) " +
                             "— falling back to the default detection area.");
        }

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
            ScannedTime = DateTimeOffset.Now
        };
        
        MobileBarcodeScanner.DispatchContinuousResult(instanceId, result);

        var myGeneration = Interlocked.Increment(ref _continuousResetGeneration);
        var resetDelay = _options.GetEffectiveOverlayResetDelay();
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now,
                                                               TimeSpan.FromMilliseconds(resetDelay)),
                                              () =>
                                              {
                                                  if (Interlocked.CompareExchange(ref _continuousResetGeneration, 0, 0) != myGeneration)
                                                      return;
                                                  _activeOverlay?.ClearOverlay();
                                              });
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
            ScannedTime = DateTimeOffset.Now
        };
        
        MobileBarcodeScanner.DispatchSingleResult(instanceId, result);
        
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, 
                                                               TimeSpan.FromMilliseconds(_delayBeforeClose)), 
                                              () => DismissOnce());
    }
    
    private void HandleOverlayBackRequested()
    {
        _isFinishing = true;
        MobileBarcodeScanner.DispatchCancel(instanceId);
        DismissOnce();
    }

    private void SetTorchInternal(bool turnOn)
    {
        if (_cameraDevice is null)
        {
            // SetupCamera hasn't assigned _cameraDevice yet - remember the request and replay it
            // from SetupCamera once the device exists, instead of silently dropping it.
            Interlocked.Exchange(ref _pendingTorchState, turnOn ? PendingTorchOn : PendingTorchOff);
            return;
        }

        if (_cameraDevice is not { HasTorch: true }) return;

        _cameraDevice.LockForConfiguration(out var error);
        if (error == null)
        {
            _cameraDevice.TorchMode = turnOn ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off;
            _cameraDevice.UnlockForConfiguration();
        }
    }

    public void SetTorch(bool turnOn)
    {
        SetTorchInternal(turnOn);
    }

    // Dismiss is idempotent because multiple independent paths can trigger it for the same
    // session (barcode found, back button, CancelScan/RequestCancel from the shared layer) —
    // all on the main queue, so a plain bool is enough; no volatile/Interlocked needed here.
    //
    // A call arriving before this controller has ever appeared (e.g. SetupCamera failing from
    // within ViewDidLoad, before the presentation transition has completed) would have UIKit
    // silently ignore DismissViewController — "presentation is in progress" — while _isDismissed
    // was already latched true, permanently blocking every later legitimate dismiss attempt. So
    // _isDismissed is only latched once a dismiss is actually issued; an earlier request is
    // remembered in _dismissPending and retried from ViewDidAppear, which is guaranteed to fire
    // once presentation has genuinely finished.
    private void DismissOnce()
    {
        if (_isDismissed) return;

        if (!_hasAppeared)
        {
            _dismissPending = true;
            return;
        }

        _isDismissed = true;
        DismissViewController(true, null);
    }

    public void CloseScanner()
    {
        DispatchQueue.MainQueue.DispatchAsync(DismissOnce);
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