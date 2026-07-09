using System.Collections.Concurrent;
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
using Java.Lang;
using Java.Util.Concurrent;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Exception = Java.Lang.Exception;
using Size = Android.Util.Size;
using MResource = _Microsoft.Android.Resource.Designer.Resource;

namespace BarcodeScanner;

[Activity(Label = "BarcodeScannerActivity",
          ScreenOrientation = ScreenOrientation.Portrait,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class BarcodeScannerActivity : FragmentActivity
{
    private static readonly ConcurrentDictionary<string, WeakReference<BarcodeScannerActivity>> ActiveInstances = new();
    
    private const int CAMERA_REQUEST_CODE = 1001;
    private string _instanceId = string.Empty;
    
    private PreviewView? _cameraPreview;
    private IListenableFuture? _cameraProviderFuture;
    private ProcessCameraProvider? _cameraProvider;
    private ICameraControl? _cameraControl;
    private View? _overlayView;
    
    private IBarcodeScanner? _barcodeScanner;
    private IActiveScannerOverlay? _activeOverlay;
    private IImageProxy? _latestImageProxy;
    private BarcodeDetectionHandler? _detectionHandler;
    private BarcodeScanningOptions _options;

    private bool _isFinishing;
    private int _delayBeforeClose;
    private volatile bool _isScanning = true;
    
    private IExecutorService? _analysisExecutor;
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(MResource.Layout.activity_scan);

        _instanceId = Intent?.GetStringExtra("scanner_instance_id") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_instanceId))
        {
            Log.Error("BarcodeScannerActivity", "CRITICAL ERROR: ScannerActivity launched without a valid InstanceId. Aborting to prevent Task hanging.");
            Finish();
            return;
        }
        
        var root = FindViewById<ConstraintLayout>(MResource.Id.scanner_container);
        if (root == null)
        {
            throw new InvalidOperationException(
                                                "[BarcodeScanner] CRITICAL: Root ConstraintLayout with id 'scanner_container' not found in activity_scan.xml. " +
                                                "Please ensure the library resources are correctly merged and the layout file has not been modified.");
        }

        _analysisExecutor = Executors.NewSingleThreadExecutor() ??
                            throw new
                                InvalidOperationException("Failed to create analysis executor. This should never happen.");
        
        _cameraPreview = FindViewById<PreviewView>(MResource.Id.camera_preview);

        ActiveInstances[_instanceId] = new WeakReference<BarcodeScannerActivity>(this);
        
        _options = MobileBarcodeScanner.GetOptions(_instanceId);
        
        _detectionHandler = new BarcodeDetectionHandler(_options);
        ApplyOptions(_options);
        SetupOverlay(root, _options);

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
        {
            SetupScanner(_options);
            SetupCamera();
        }
        else
        {
            ActivityCompat.RequestPermissions(this, [Manifest.Permission.Camera], CAMERA_REQUEST_CODE);
        }
    }

    protected override void OnDestroy()
    {
        _analysisExecutor.Shutdown();
        _analysisExecutor = null;
        
        base.OnDestroy();
        _cameraProvider?.UnbindAll();
        _barcodeScanner?.Dispose();
        
        if (_overlayView is { Parent: ViewGroup parent })
        {
            parent.RemoveView(_overlayView);
            _overlayView.Dispose();
        }
        
        if (!string.IsNullOrWhiteSpace(_instanceId))
        {
            ActiveInstances.TryRemove(_instanceId, out _);
        }

        if (_isFinishing) 
            return;
        
        if(!string.IsNullOrWhiteSpace(_instanceId))
            MobileBarcodeScanner.DispatchCancel(_instanceId);
    }

    private void ApplyOptions(BarcodeScanningOptions options)
    {
        _delayBeforeClose = options.DelayBeforeScannerClose;
    }
    
    private void SetupOverlay(ConstraintLayout root, BarcodeScanningOptions options)
    {
        View? overlayView;
        if (options.CustomOverlayFactory != null)
        {
            var overlayInstance = options.CustomOverlayFactory(this);
            if (overlayInstance is not View view)
            {
                throw new InvalidOperationException("CustomOverlayFactory must return Android.Views.View");
            }

            overlayView = view;

            if (overlayInstance is IActiveScannerOverlay activeOverlay)
            {
                _activeOverlay = activeOverlay;
            }
        }
        else
        {
            var overlayContainer = new BarcodeScannerOverlayWithButtons(this);

            overlayContainer.OnBackRequested += () =>
            {
                _isFinishing = true;
                MobileBarcodeScanner.DispatchCancel(_instanceId);
                Finish();
            };
            overlayContainer.OnTorchToggle += (isOn) =>
            {
                SetTorchState(_instanceId, isOn);
            };
            
            overlayView = overlayContainer;
            _activeOverlay = overlayContainer;
        }
        
        _overlayView = overlayView;
        overlayView.Id = View.GenerateViewId();

        root.AddView(overlayView);
        var constraintSet = new ConstraintSet();
        constraintSet.Clone(root);
            
        constraintSet.Connect(overlayView.Id, ConstraintSet.Top, ConstraintSet.ParentId, ConstraintSet.Top);
        constraintSet.Connect(overlayView.Id, ConstraintSet.Bottom, ConstraintSet.ParentId, ConstraintSet.Bottom);
        constraintSet.Connect(overlayView.Id, ConstraintSet.Start, ConstraintSet.ParentId, ConstraintSet.Start);
        constraintSet.Connect(overlayView.Id, ConstraintSet.End, ConstraintSet.ParentId, ConstraintSet.End);

        constraintSet.ApplyTo(root);
        
        if(options.RegionOfInterest is {IsValid: true} roi)
            _activeOverlay?.SyncRegionOfInterest(roi);
        
        #if DEBUG
        if (options.RegionOfInterest is { IsValid: true } && options.CustomOverlayFactory is not null)
        {
            Log.Warn("BarcodeScanner",
                     "RegionOfInterest is set together with custom overlay. The drawn viewfinder may not" +
                     "match the actual scanning area unless the overlay implements IActiveScannerOverlay.SyncRegionOfInterest.");
        }
        #endif
    }

    private void SetupScanner(BarcodeScanningOptions options)
    {
        var mlFormats = options.PossibleFormats
                                .Select(f => f.ToMlKitFormat())
                                .Distinct()
                                .ToArray();
        int firstFormat;
        int[] otherFormats;

        if (mlFormats.Length == 0 || mlFormats.Contains(Barcode.FormatAllFormats))
        {
            firstFormat = Barcode.FormatAllFormats;
            otherFormats = [];
        }
        else
        {
            firstFormat = mlFormats[0];
            otherFormats = mlFormats.Skip(1).ToArray();
        }

        var mlOptions = new BarcodeScannerOptions.Builder()
                        .SetBarcodeFormats(firstFormat, otherFormats)
                        .Build();
        _barcodeScanner = BarcodeScanning.GetClient(mlOptions);
    }

    private void SetupCamera()
    {
        if (_barcodeScanner == null)
            return;
        
        _cameraProviderFuture = ProcessCameraProvider.GetInstance(this);
        _cameraProviderFuture?.AddListener(new Runnable(SetupCameraProvider),
                                           ContextCompat.GetMainExecutor(this));
    }

    private void SetupCameraProvider()
    {
        try
        {
            _cameraProvider = _cameraProviderFuture?.Get() as ProcessCameraProvider;
            if(_cameraProvider == null)
                throw new Exception("Не удалось получить экземпляр ProcessCameraProvider");
            BindCameraUseCases(_cameraProvider);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CameraX error: {ex.Message}");
            if(!string.IsNullOrWhiteSpace(_instanceId))
                MobileBarcodeScanner.DispatchError(_instanceId, $"Ошибка инициализации камеры: {ex.Message}");
            Finish();
        }
    }

    private void BindCameraUseCases(ProcessCameraProvider cameraProvider)
    {
        var preview = CreatePreviewUseCase();
        var cameraSelector = CreateCameraSelector();
        var imageAnalysis = CreateImageAnalysisUseCase();
        
        cameraProvider.UnbindAll();
        var camera = cameraProvider.BindToLifecycle(this, cameraSelector, preview, imageAnalysis);
        _cameraControl = camera.CameraControl;
    }
        
    private Preview CreatePreviewUseCase()
    {
        var preview = new Preview.Builder().Build() ?? throw new NullReferenceException("Не удалось создать Preview");
        preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(this), _cameraPreview?.SurfaceProvider);
        return preview;
    }

    private CameraSelector CreateCameraSelector()
    {
        return CameraSelector.DefaultBackCamera 
               ?? throw new ArgumentNullException(nameof(CameraSelector), "Задняя камера недоступна на этом устройстве");
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
                            .Build() ?? throw new Exception("Не удалось создать ImageAnalysis");

        if (_barcodeScanner != null)
        {
            imageAnalysis.SetAnalyzer(_analysisExecutor,
                                      new BarcodeAnalyzer(
                                                          _barcodeScanner,
                                                          OnBarcodesFound,
                                                          _detectionHandler!.ShouldProcessFrame,
                                                          proxy => { _latestImageProxy = proxy; }));
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
        if (!_isScanning || _detectionHandler is null || _cameraPreview is null)
            return;
        
        var barcodeDataList = new List<BarcodeData>();
        foreach (var barcode in barcodes)
        {
            if (string.IsNullOrWhiteSpace(barcode.RawValue)) 
                continue; 
            
            var screenPoints = GetMappedPoints(barcode);

            if (screenPoints is null) 
                continue;
            
            var symbology = barcode.Format.ToLocalFormat();
            barcodeDataList.Add(new BarcodeData(
                                                barcode.RawValue,
                                                barcode.DisplayValue,
                                                symbology,
                                                screenPoints));
        }
        
        var roiRectF = GetCurrentRoiRectF();
        var roi = new RoiBounds(roiRectF.Left, roiRectF.Top, roiRectF.Right, roiRectF.Bottom);
        
        var currentTimeMs = SystemClock.ElapsedRealtime();
        var result = _detectionHandler.Process(barcodeDataList, currentTimeMs, roi);
        if (result is null) return;

        HandleResult(result.Value);
    }

    private RectF GetCurrentRoiRectF()
    {
        if (_options.RegionOfInterest.HasValue && _cameraPreview != null)
            return _options.RegionOfInterest.Value.ToRectF(_cameraPreview.Width, _cameraPreview.Height);
        
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
            ScannedTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
        };
        
        switch (result.ScanType)
        {
            case ScanType.Continuous:
                MobileBarcodeScanner.DispatchContinuousResult(_instanceId, codeResult);
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    RunOnUiThread(() =>
                    {
                        _activeOverlay?.ClearOverlay();
                    });
                }, _delayBeforeClose);
                break;
            case ScanType.OneShot:
                _isScanning = false;
                _isFinishing = true;
                MobileBarcodeScanner.DispatchSingleResult(_instanceId, codeResult);
                new Handler(Looper.MainLooper).PostDelayed(Finish, _delayBeforeClose);
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
         
        var srcPoints = new float[8];
        for (var i = 0; i < 4; i++)
        {
            srcPoints[i * 2] = points[i].X;
            srcPoints[i * 2 + 1] = points[i].Y;
        }

        var matrix = MatrixHelper.GetCorrectionMatrix(_latestImageProxy,
                                                      _cameraPreview);
        if (matrix == null)
            return null;

        var mappedPoints = new float[8];
        matrix.MapPoints(mappedPoints, srcPoints);
        return mappedPoints;
    }
    
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != CAMERA_REQUEST_CODE || grantResults.Length <= 0 || grantResults[0] != Permission.Granted)
        {
            MobileBarcodeScanner.DispatchError(_instanceId, "Camera permission is not granted.");
            Finish();
            return;
        }

        if (string.IsNullOrWhiteSpace(_instanceId)) 
            return;
        var options = MobileBarcodeScanner.GetOptions(_instanceId);
        SetupScanner(options);
        SetupCamera();
    }

    public override void OnBackPressed()
    {
        base.OnBackPressed();
        if(!string.IsNullOrWhiteSpace(_instanceId))
            MobileBarcodeScanner.DispatchCancel(_instanceId);
    }
    
    public static void FinishByInstanceId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;

        if (ActiveInstances.TryGetValue(instanceId, out var weakRef) &&
            weakRef.TryGetTarget(out var activity))
        {
            activity.RunOnUiThread(() =>
            {
                if (activity is { IsFinishing: false, IsDestroyed: false })
                {
                    activity.Finish();
                }
            });
        }
    }

    internal static void SetTorchState(string instanceId, bool turnTorchOn)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        if (ActiveInstances.TryGetValue(instanceId, out var weakRef) &&
            weakRef.TryGetTarget(out var activity))
        {
            activity.RunOnUiThread(() => activity?._cameraControl?.EnableTorch(turnTorchOn));
        }
    }
}