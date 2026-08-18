using Android;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.ConstraintLayout.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using BarcodeScanner.Analysers;
using BarcodeScanner.Enums;
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;
using BarcodeScanner.Ui.Views;
using Google.Common.Util.Concurrent;
using Java.Util.Concurrent;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Size = Android.Util.Size;
using MResource = _Microsoft.Android.Resource.Designer.Resource;

namespace BarcodeScanner;

[Activity(Label = "BarcodeScannerActivity",
          ScreenOrientation = ScreenOrientation.Portrait,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class BarcodeScannerActivity : FragmentActivity, IScannerPlatform
{
    private const int CAMERA_REQUEST_CODE = 1001;
    private string _instanceId = string.Empty;
    
    private PreviewView? _cameraPreview;
    private IListenableFuture? _cameraProviderFuture;
    private ProcessCameraProvider? _cameraProvider;
    private ICameraControl? _cameraControl;
    private View? _overlayView;
    private ConstraintLayout? _root;
    
    private IBarcodeScanner? _barcodeScanner;
    private IActiveScannerOverlay? _activeOverlay;
    private FrameGeometry? _latestFrameGeometry;
    private BarcodeDetectionHandler? _detectionHandler;
    private BarcodeScanningOptions _options;

    private bool _isFinishing;
    private int _delayBeforeClose;
    private volatile bool _isScanning = true;
    
    private IExecutorService? _analysisExecutor;
    private ImageAnalysis? _imageAnalysis;
    private BarcodeScannerOverlayWithButtons? _overlayContainer;
    private Handler? _singleShotHandler;
    private Handler? _continuousHandler;
    private BarcodeAnalyzer? _barcodeAnalyser;
    private Preview? _previewUseCase;
    private CameraProviderRunnable? _cameraProviderRunnable;
    
    private Action<List<Barcode>>? _onBarcodeDetectedDelegate;
    private Func<bool>? _shouldProcessFrameDelegate;
    private Action<FrameGeometry>? _onImageInfoDelegate;

    // Reused across OnBarcodesFoundInternal/GetMappedPoints calls instead of allocating fresh each
    // time (AND-03). Safe: OnBarcodesFoundInternal always runs via RunOnUiThread (single UI thread,
    // Looper messages processed strictly sequentially), and each buffer is fully consumed before
    // the method that filled it returns — no cross-call or cross-thread retention.
    private readonly List<BarcodeData> _barcodeDataBuffer = [];
    private readonly float[] _srcPointsBuffer = new float[8];
    private readonly Matrix _correctionMatrix = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(MResource.Layout.activity_scan);

        _instanceId = Intent?.GetStringExtra("scanner_instance_id") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_instanceId))
        {
            Log.Error("BarcodeScannerActivity", "CRITICAL ERROR: ScannerActivity launched without a valid InstanceId. Aborting.");
            Finish();
            return;
        }

        // Attached as early as possible so a CancelScan()/ToggleTorch() call racing with the
        // rest of OnCreate is never silently dropped due to PlatformSession still being null.
        MobileBarcodeScanner.AttachPlatformSession(_instanceId, new AndroidScannerSession(new WeakReference<IScannerPlatform>(this)));
        //LeakTracker.Track(this);

        _root = FindViewById<ConstraintLayout>(MResource.Id.scanner_container);
        if (_root == null)
        {
            MobileBarcodeScanner.DispatchError(_instanceId,
                                               "[BarcodeScanner] CRITICAL: Root ConstraintLayout with id 'scanner_container' not found in activity_scan.xml. " +
                                               "Please ensure the library resources are correctly merged and the layout file has not been modified.");
            Finish();
            return;
        }

        _analysisExecutor = Executors.NewSingleThreadExecutor();
        if (_analysisExecutor == null)
        {
            MobileBarcodeScanner.DispatchError(_instanceId, "Failed to create analysis executor.");
            Finish();
            return;
        }

        _cameraPreview = FindViewById<PreviewView>(MResource.Id.camera_preview);

        _options = MobileBarcodeScanner.GetOptions(_instanceId);
        
        _detectionHandler = new BarcodeDetectionHandler(_options);
        ApplyOptions(_options);
        if (!SetupOverlay(_options))
            return;

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
        {
            if (!SetupScanner(_options))
                return;
            SetupCamera();
        }
        else
        {
            ActivityCompat.RequestPermissions(this, [Manifest.Permission.Camera], CAMERA_REQUEST_CODE);
        }
    }

    protected override void OnDestroy()
    {
        System.Diagnostics.Debug
              .WriteLine($"[BarcodeScanner] OnDestroy called for BarcodeScannerActivity instance {_instanceId}");
        
        if (!string.IsNullOrWhiteSpace(_instanceId))
        {
            System.Diagnostics.Debug.WriteLine("[BarcodeScanner] Calling Dispatch cancel");
            MobileBarcodeScanner.DispatchCancel(_instanceId);
        }

        try
        {
            SafeCleanup("barcodeAnalyser.MarkAsDisposed",
                        () => _barcodeAnalyser?.MarkAsDisposed());

            SafeCleanup("imageAnalysis.ClearAnalyzer",
                        () => _imageAnalysis?.ClearAnalyzer());

            // Drain whatever Analyze() call is already queued or running on the background
            // executor before disposing anything it touches below (_barcodeScanner,
            // _barcodeAnalyser, _imageAnalysis, _cameraProvider) — MarkAsDisposed/ClearAnalyzer
            // stop *new* frames from being scheduled, but don't wait for an in-flight one to
            // finish, so disposal further down could otherwise race with it.
            SafeCleanup("Analysis executor shutdown", () =>
            {
                _analysisExecutor?.Shutdown();
                _analysisExecutor?.AwaitTermination(300, TimeUnit.Milliseconds);
                _analysisExecutor = null;
            });

            SafeCleanup("cameraProvider.UnbindAll",
                        () => _cameraProvider?.UnbindAll());
            
            SafeCleanup("Clearing Preview SurfaceProvider", () => 
            { 
                _previewUseCase?.SetSurfaceProvider(ContextCompat.GetMainExecutor(this), null);
                _previewUseCase?.Dispose();
                _previewUseCase = null;
            });
            
            SafeCleanup("Disposing MLKit.BarcodeScanner", () => 
            { 
                _barcodeScanner?.Dispose(); 
                _barcodeScanner = null; 
            });
            
            SafeCleanup("Disposing BarcodeAnalyzer", () =>
            {
                _barcodeAnalyser?.Dispose();
                _barcodeAnalyser = null;
            });
            
            SafeCleanup("Disposing PreviewView", () =>
            {
                if (_cameraPreview is {Parent: ViewGroup previewParent})
                    previewParent.RemoveView(_cameraPreview);
                _cameraPreview?.Dispose();
                _cameraPreview = null;
            });
            
            SafeCleanup($"Remove overlays from parent", () =>
            {
                if (_overlayView != null && _root != null)
                {
                    _root.RemoveView(_overlayView);
                }
                if (_overlayContainer is not null)
                {
                    // Only dispose the overlay the library itself created. _overlayView can also
                    // be a consumer-supplied view from CustomOverlayFactory — disposing that would
                    // be an ownership violation, and if the consumer caches/reuses the instance
                    // across scan sessions, the next session would touch an already-disposed ACW.
                    _overlayContainer.OnBackRequested -= CancelScan;
                    _overlayContainer.OnTorchToggle -= SetTorch;
                    _overlayContainer.Dispose();
                }
                _overlayView = null;
                _overlayContainer = null;
            });
            
            SafeCleanup("Disposing Handlers", () =>
            {
                _singleShotHandler?.RemoveCallbacksAndMessages(null);
                _singleShotHandler?.Dispose();
                _singleShotHandler = null;
                _continuousHandler?.RemoveCallbacksAndMessages(null);
                _continuousHandler?.Dispose();
                _continuousHandler = null;
            });
            
            SafeCleanup("Cancelling camera future", () =>
            {
                _cameraProviderFuture?.Cancel(true);
                _cameraProviderFuture?.Dispose();
                _cameraProviderFuture = null; 
            });
            
            SafeCleanup("Disposing camera provider runnable", () =>
            {
                _cameraProviderRunnable?.Dispose();
                _cameraProviderRunnable = null; 
            });
            
            SafeCleanup("Dispose camera control", () =>
            {
                _cameraControl?.Dispose();
                _cameraControl = null;
            });
            
            SafeCleanup("Dispose camera provider", () =>
            {
                _cameraProvider?.Dispose();
                _cameraProvider = null;
            });
            
            SafeCleanup("Dispose image analysis", () =>
            {
                _imageAnalysis?.Dispose();
                _imageAnalysis = null;
            });
            
            _detectionHandler = null;
            _activeOverlay = null;
            _options = null;
            _root = null;
            _onBarcodeDetectedDelegate = null;
            _shouldProcessFrameDelegate = null;
            _onImageInfoDelegate = null;
            Volatile.Write(ref _latestFrameGeometry, null);
            
            System.Diagnostics.Debug.WriteLine("[BarcodeScanner] Cleanup completed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] Android cleanup error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] StackTrace: {ex.StackTrace}");
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("[BarcodeScanner] Calling base.OnDestroy");
            base.OnDestroy();
        }
    }

    private void SafeCleanup(string step, Action action)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] Cleanup Step:{step}");
            action.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] Cleanup Step:{step} Error: {ex.Message}");
        }
    }

    private void ApplyOptions(BarcodeScanningOptions options)
    {
        _delayBeforeClose = options.DelayBeforeScannerClose;
    }
    
    private bool SetupOverlay(BarcodeScanningOptions options)
    {
        // The whole body is guarded because it calls into arbitrary consumer code
        // (CustomOverlayFactory) with no way to know what it might throw, plus a handful of
        // platform calls (AddView, ConstraintSet) that can also fail. SetupOverlay is called
        // from OnCreate, which has no enclosing try/catch anywhere above it — an unhandled
        // exception here would crash the host app instead of following this codebase's own
        // "setup failure -> DispatchError + Finish()" convention (already used a few lines
        // below for the two explicit validation failures).
        try
        {
            View? overlayView;
            if (options.CustomOverlayFactory != null)
            {
                var overlayInstance = options.CustomOverlayFactory(this);
                if (overlayInstance is not View view)
                {
                    MobileBarcodeScanner.DispatchError(_instanceId, "CustomOverlayFactory must return Android.Views.View");
                    Finish();
                    return false;
                }

                overlayView = view;

                if (overlayInstance is IActiveScannerOverlay activeOverlay)
                {
                    _activeOverlay = activeOverlay;
                }
            }
            else
            {
                _overlayContainer = new BarcodeScannerOverlayWithButtons(this);

                _overlayContainer.OnBackRequested += CancelScan;
                _overlayContainer.OnTorchToggle += SetTorch;

                overlayView = _overlayContainer;
                _activeOverlay = _overlayContainer;
            }

            if (overlayView.Parent is not null)
            {
                MobileBarcodeScanner.DispatchError(_instanceId,
                    "CustomOverlayFactory must return a fresh, unattached View for every scan session.");
                Finish();
                return false;
            }

            _overlayView = overlayView;
            overlayView.Id = View.GenerateViewId();

            _root?.AddView(overlayView);
            var constraintSet = new ConstraintSet();
            constraintSet.Clone(_root);

            constraintSet.Connect(overlayView.Id, ConstraintSet.Top, ConstraintSet.ParentId, ConstraintSet.Top);
            constraintSet.Connect(overlayView.Id, ConstraintSet.Bottom, ConstraintSet.ParentId, ConstraintSet.Bottom);
            constraintSet.Connect(overlayView.Id, ConstraintSet.Start, ConstraintSet.ParentId, ConstraintSet.Start);
            constraintSet.Connect(overlayView.Id, ConstraintSet.End, ConstraintSet.ParentId, ConstraintSet.End);

            constraintSet.ApplyTo(_root);

            if (options.RegionOfInterest is { IsValid: true } roi)
                _activeOverlay?.SyncRegionOfInterest(roi);

            #if DEBUG
            if (options.RegionOfInterest is { IsValid: true } && options.CustomOverlayFactory is not null)
            {
                Log.Warn("BarcodeScanner",
                         "RegionOfInterest is set together with custom overlay. The drawn viewfinder may not" +
                         "match the actual scanning area unless the overlay implements IActiveScannerOverlay.SyncRegionOfInterest.");
            }
            #endif

            return true;
        }
        catch (System.Exception ex)
        {
            // Deliberately no cleanup here: OnDestroy is the sole cleanup owner. If we failed
            // before _overlayView/_overlayContainer were assigned, OnDestroy's cleanup already
            // no-ops on them (still null); if we failed after, OnDestroy's existing RemoveView
            // + unsubscribe + Dispose already handles it correctly. Cleaning up here too would
            // double-dispose/double-unsubscribe. Finish() below reliably drives OnDestroy.
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] SetupOverlay failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] StackTrace: {ex.StackTrace}");
            MobileBarcodeScanner.DispatchError(_instanceId, $"Failed to set up scanner overlay: {ex.Message}");
            Finish();
            return false;
        }
    }

    private bool SetupScanner(BarcodeScanningOptions options)
    {
        // Guarded for the same reason as SetupOverlay: called from OnCreate/OnRequestPermissionsResult,
        // neither of which has an enclosing try/catch, and BarcodeScanning.GetClient(...) reaches into
        // Play Services / ML Kit's native bridge, which can genuinely throw (AND-01 in CONTEXT.md).
        try
        {
            var mlFormats = ResolveMlKitFormats(options.PossibleFormats);

            var mlOptions = new BarcodeScannerOptions.Builder()
                            .SetBarcodeFormats(mlFormats[0], mlFormats.Skip(1).ToArray())
                            .Build();
            _barcodeScanner = BarcodeScanning.GetClient(mlOptions);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] SetupScanner failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] StackTrace: {ex.StackTrace}");
            MobileBarcodeScanner.DispatchError(_instanceId, $"Failed to set up barcode scanner: {ex.Message}");
            Finish();
            return false;
        }
    }

    /// <summary>
    /// Builds the ML Kit format list strictly from known barcode symbologies. Deliberately
    /// avoids Barcode.FormatAllFormats: that wildcard also detects symbologies with no iOS
    /// equivalent (e.g. UPC-A, which AVFoundation reports as EAN-13), which would break
    /// cross-platform parity for a library that only claims to detect the ML Kit / AVFoundation
    /// intersection.
    /// </summary>
    private static int[] ResolveMlKitFormats(IEnumerable<BarcodeSymbology> possibleFormats)
    {
        var symbologies = possibleFormats as ICollection<BarcodeSymbology> ?? possibleFormats.ToArray();

        var effective = symbologies.Count == 0 || symbologies.Contains(BarcodeSymbology.AllSymbologies)
            ? BarcodeSymbologySet.AllConcrete
            : symbologies;

        var mlFormats = effective
                        .Select(f => f.ToMlKitFormat())
                        .Where(f => f != Barcode.FormatUnknown)
                        .Distinct()
                        .ToArray();

        return mlFormats.Length > 0
            ? mlFormats
            : BarcodeSymbologySet.AllConcrete.Select(f => f.ToMlKitFormat()).Distinct().ToArray();
    }

    private void SetupCamera()
    {
        if (_barcodeScanner == null)
            return;

        try
        {
            _cameraProviderFuture = ProcessCameraProvider.GetInstance(this);
            _cameraProviderRunnable = new CameraProviderRunnable(new WeakReference<BarcodeScannerActivity>(this));
            _cameraProviderFuture?.AddListener(_cameraProviderRunnable,
                                               ContextCompat.GetMainExecutor(this));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] SetupCamera failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] StackTrace: {ex.StackTrace}");
            MobileBarcodeScanner.DispatchError(_instanceId, $"Failed to set up camera: {ex.Message}");
            Finish();
        }
    }

    private void SetupCameraProvider()
    {
        if (IsDestroyed || IsFinishing) return;
        try
        {
            _cameraProvider = _cameraProviderFuture?.Get() as ProcessCameraProvider;
            if(_cameraProvider == null)
                throw new Exception("Failed to obtain ProcessCameraProvider instance");
            BindCameraUseCases(_cameraProvider);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CameraX error: {ex.Message}");
            if(!string.IsNullOrWhiteSpace(_instanceId))
                MobileBarcodeScanner.DispatchError(_instanceId, $"Camera initialization failed: {ex.Message}");
            Finish();
        }
    }

    private void BindCameraUseCases(ProcessCameraProvider cameraProvider)
    {
        _previewUseCase = CreatePreviewUseCase();
        var cameraSelector = CreateCameraSelector();
        _imageAnalysis = CreateImageAnalysisUseCase();
        
        cameraProvider.UnbindAll();
        var camera = cameraProvider.BindToLifecycle(this, cameraSelector, _previewUseCase, _imageAnalysis);
        _cameraControl = camera.CameraControl;
    }
        
    private Preview CreatePreviewUseCase()  
    {
        var preview = new Preview.Builder().Build() ?? throw new NullReferenceException("Failed to create Preview");
        preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(this), _cameraPreview?.SurfaceProvider);
        return preview;
    }

    private CameraSelector CreateCameraSelector()
    {
        return CameraSelector.DefaultBackCamera
               ?? throw new ArgumentNullException(nameof(CameraSelector), "Back camera is not available on this device");
    }

    private ImageAnalysis CreateImageAnalysisUseCase()
    {
        var resolutionSelector = new ResolutionSelector.Builder()
                                 .SetResolutionStrategy(new ResolutionStrategy(
                                                                               new Size(1280, 720),
                                                                               ResolutionStrategy.FallbackRuleClosestHigher))?
                                 .Build();
        
        var imageAnalysis = new ImageAnalysis.Builder()
                            .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)?
                            .SetResolutionSelector(resolutionSelector)?
                            .Build() ?? throw new Exception("Failed to create ImageAnalysis");

        if (_barcodeScanner != null)
        {
            _onBarcodeDetectedDelegate = OnBarcodesFound;
            _shouldProcessFrameDelegate = _detectionHandler!.ShouldProcessFrame;
            _onImageInfoDelegate = geometry => Volatile.Write(ref _latestFrameGeometry, geometry);

            _barcodeAnalyser = new BarcodeAnalyzer(_barcodeScanner,
                                                   new WeakReference<Action<List<Barcode>>>(_onBarcodeDetectedDelegate),
                                                   new WeakReference<Func<bool>>(_shouldProcessFrameDelegate),
                                                   new WeakReference<Action<FrameGeometry>>(_onImageInfoDelegate));
            imageAnalysis.SetAnalyzer(_analysisExecutor,
                                      _barcodeAnalyser);
        }
        else
        {
            throw new Exception("Cannot setup image analysis use case! BarcodeScanner is null!");
        }

        return imageAnalysis;
    }

    private void OnBarcodesFound(List<Barcode> barcodes)
    {
        RunOnUiThread(() => OnBarcodesFoundInternal(barcodes));
    }

    private void OnBarcodesFoundInternal(List<Barcode> barcodes)
    {
        if (!_isScanning || _isFinishing || _detectionHandler is null || _cameraPreview is null)
            return;
        
        _barcodeDataBuffer.Clear();
        foreach (var barcode in barcodes)
        {
            if (string.IsNullOrWhiteSpace(barcode.RawValue))
                continue;

            var screenPoints = GetMappedPoints(barcode);

            if (screenPoints is null)
                continue;

            var symbology = barcode.Format.ToLocalFormat();
            _barcodeDataBuffer.Add(new BarcodeData(
                                                   barcode.RawValue,
                                                   barcode.DisplayValue,
                                                   symbology,
                                                   screenPoints));
        }

        var roiRectF = GetCurrentRoiRectF();
        var roi = new RoiBounds(roiRectF.Left, roiRectF.Top, roiRectF.Right, roiRectF.Bottom);

        var result = _detectionHandler.Process(_barcodeDataBuffer, roi);
        if (result is null) return;

        HandleResult(result.Value);
    }

    private RectF GetCurrentRoiRectF()
    {
        if (_options.RegionOfInterest is { } roi)
        {
            if (roi.IsValid && _cameraPreview != null)
                return roi.ToRectF(_cameraPreview.Width, _cameraPreview.Height);

            if (!roi.IsValid)
                System.Diagnostics.Debug.WriteLine("[BarcodeScanner] Warning: RegionOfInterest is set but invalid " +
                                                    "(coordinates must be in [0,1] with Right > Left and Bottom > Top) " +
                                                    "— falling back to the default detection area.");
        }

        return _activeOverlay is not null
            ? _activeOverlay.GetViewfinderRect().ToRectF()
            : new RectF(0, 0, _cameraPreview?.Width ?? 0, _cameraPreview?.Height ?? 0);
    }
    
    private void HandleResult(DetectionResult result)
    {
        if (result.ShouldResetOverlay)
        {
            RunOnUiThread(() => _activeOverlay?.ClearOverlay());
            return;
        }

        var codeResult = new BarcodeResult
        {
            RawValue = result.RawValue,
            DisplayValue = result.DisplayValue,
            Symbology = result.Symbology,
            ErrorMessage = null,
            Status = ScanStatus.Success,
            ScannedTime = DateTimeOffset.Now
        };
        
        switch (result.ScanType)
        {
            case ScanType.Continuous:
                MobileBarcodeScanner.DispatchContinuousResult(_instanceId, codeResult);
                _continuousHandler ??= new Handler(Looper.MainLooper);
                _continuousHandler.RemoveCallbacksAndMessages(null);
                _continuousHandler.PostDelayed(() =>
                {
                    RunOnUiThread(() =>
                    {
                        _activeOverlay?.ClearOverlay();
                    });
                }, _options.GetEffectiveOverlayResetDelay());
                break;
            case ScanType.OneShot:
                _isScanning = false;
                _isFinishing = true;
                MobileBarcodeScanner.DispatchSingleResult(_instanceId, codeResult);
                _singleShotHandler ??= new Handler(Looper.MainLooper);
                _singleShotHandler.RemoveCallbacksAndMessages(null);
                _singleShotHandler.PostDelayed(Finish, _delayBeforeClose);
                break;
        }
        
        if (result.SmoothedPoints != null)
        {
            RunOnUiThread(() => _activeOverlay?.UpdateOverlay(result.RawValue, result.SmoothedPoints));
        }
    }
    
    private float[]? GetMappedPoints(Barcode barcode)
    {
        var points = barcode.GetCornerPoints();
        if (points is null or {Length: < 4}) return null; 
         
        for (var i = 0; i < 4; i++)
        {
            _srcPointsBuffer[i * 2] = points[i].X;
            _srcPointsBuffer[i * 2 + 1] = points[i].Y;
        }

        var geometry = Volatile.Read(ref _latestFrameGeometry);
        if (geometry is null)
            return null;

        var matrix = MatrixHelper.GetCorrectionMatrix(geometry.Width, geometry.Height, geometry.RotationDegrees,
                                                      _cameraPreview, _correctionMatrix);
        if (matrix == null)
            return null;

        var mappedPoints = new float[8];
        matrix.MapPoints(mappedPoints, _srcPointsBuffer);
        return mappedPoints;
    }
    
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // Mirrors the guard already used for the other async OS callback in this file
        // (SetupCameraProvider/CameraProviderRunnable) - defensive consistency for the same
        // class of "callback lands after Finish() but before/without OnDestroy fully tearing
        // things down" scenario (CONC-08 in CONTEXT.md), not a confirmed reproduction: a live
        // device test found that once OnDestroy has actually completed, Android does not
        // deliver a pending permission result to this activity at all.
        if (IsDestroyed || IsFinishing)
            return;

        if (requestCode != CAMERA_REQUEST_CODE || grantResults.Length <= 0 || grantResults[0] != Permission.Granted)
        {
            MobileBarcodeScanner.DispatchError(_instanceId, "Camera permission is not granted.");
            Finish();
            return;
        }

        if (string.IsNullOrWhiteSpace(_instanceId))
            return;
        var options = MobileBarcodeScanner.GetOptions(_instanceId);
        if (!SetupScanner(options))
            return;
        SetupCamera();
    }

    public override void OnBackPressed()
    {
        if(!string.IsNullOrWhiteSpace(_instanceId))
            MobileBarcodeScanner.DispatchCancel(_instanceId);
        base.OnBackPressed();
    }

    public void CloseScanner()
    {
        if(!IsFinishing && !IsDestroyed)
            RunOnUiThread(Finish);
    }

    public void SetTorch(bool turnOn)
    {
        RunOnUiThread(() => _cameraControl?.EnableTorch(turnOn));
    }

    private void CancelScan()
    {
        _isFinishing = true;
        MobileBarcodeScanner.DispatchCancel(_instanceId);
        Finish();
    }
    
    private sealed class CameraProviderRunnable(WeakReference<BarcodeScannerActivity> activityRef)
        : Java.Lang.Object, Java.Lang.IRunnable
    {
        public void Run()
        {
            if (!activityRef.TryGetTarget(out var activity))
                return;
                
            if (activity.IsDestroyed || activity.IsFinishing)
                return;
                
            activity.SetupCameraProvider();
        }
    }
}