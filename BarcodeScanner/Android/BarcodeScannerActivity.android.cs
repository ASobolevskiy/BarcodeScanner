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
using BarcodeScanner.Helpers;
using BarcodeScanner.Models;
using BarcodeScanner.Shared.Enums;
using BarcodeScanner.Ui.Views;
using Google.Common.Util.Concurrent;
using Java.Lang;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Exception = Java.Lang.Exception;
using Size = Android.Util.Size;
using MResource = _Microsoft.Android.Resource.Designer.Resource;

namespace BarcodeScanner;

[Activity(Label = "BarcodeScannerActivity",
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

    private bool _isFinishing;
    private bool _isContinuousScan;
    private int _delayBetweenScans;
    private int _delayBetweenFrames;
    private int _delayBeforeAnalyze;
    private int _delayBeforeClose;
    private long _lastScanResultTime;
    
    private readonly Matrix _tempMatrix = new();
    private readonly Dictionary<string, BarcodeBox> _lastBoxParams = new();
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(MResource.Layout.activity_scan);

        _instanceId = Intent?.GetStringExtra("scanner_instance_id") ?? string.Empty;
        if (string.IsNullOrEmpty(_instanceId))
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
        
        _cameraPreview = FindViewById<PreviewView>(MResource.Id.camera_preview);

        ActiveInstances[_instanceId] = new WeakReference<BarcodeScannerActivity>(this);
        
        var options = MobileBarcodeScanner.GetOptions(_instanceId);
        ApplyOptions(options);
        
        SetupOverlay(root, options);

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
        {
            SetupScanner(options);
            SetupCamera();
        }
        else
        {
            ActivityCompat.RequestPermissions(this, [Manifest.Permission.Camera], CAMERA_REQUEST_CODE);
        }
    }

    protected override void OnDestroy()
    {
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
        _isContinuousScan = options.ScannerMode == BarcodeScanningOptions.ScanType.Continuous;
        _delayBeforeAnalyze = options.DelayBeforeAnalyzingFrames;
        _delayBeforeClose = options.DelayBeforeScannerClose;
        _delayBetweenFrames = options.DelayBetweenAnalyzingFrames;
        _delayBetweenScans = options.DelayBetweenContinuousScans;
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
            var throttler = new FrameThrottler(_delayBeforeAnalyze,
                                               _delayBetweenFrames);
            imageAnalysis.SetAnalyzer(
                                      ContextCompat.GetMainExecutor(this),
                                      new BarcodeAnalyzer(
                                                          _barcodeScanner,
                                                          OnBarcodeFound,
                                                          OnBarcodeVisualUpdate,
                                                          IsInContinuousCooldown,
                                                          throttler,
                                                          proxy => { _latestImageProxy = proxy; }));
        }
        else
        {
            throw new Exception("Cannot setup image analysis use case! BarcodeScanner is null!");
        }

        return imageAnalysis;
    }

    private void OnBarcodeFound(Barcode? barcode)
    {
        if (barcode == null || string.IsNullOrWhiteSpace(_instanceId))
            return;

        if (_isContinuousScan)
        {
            HandleBarcodeFoundInContinuousMode(barcode);
        }
        else
        {
            HandleBarcodeFoundInOneShotMode(barcode);
        }
    }

    private void HandleBarcodeFoundInContinuousMode(Barcode barcode)
    {
        var currentTime = SystemClock.ElapsedRealtime();
        
        if (IsInContinuousCooldown()) 
            return;
        
        _lastScanResultTime = currentTime;
        var result = new BarcodeResult
        {
            Status = ScanStatus.Success,
            Symbology = barcode.Format.ToLocalFormat(),
            RawValue = barcode.RawValue,
            DisplayValue = barcode.DisplayValue,
            ScannedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        MobileBarcodeScanner.DispatchContinuousResult(_instanceId, result);
        
        if (Looper.MainLooper != null) 
            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                RunOnUiThread(() =>
                {
                    _activeOverlay?.ClearOverlay();
                });
            },_delayBeforeClose);
        
    }
    
    private bool IsInContinuousCooldown()
    {
        if (!_isContinuousScan) 
            return false;
    
        var currentTime = SystemClock.ElapsedRealtime();
        return (currentTime - _lastScanResultTime) < _delayBetweenScans;
    }
    
    private void HandleBarcodeFoundInOneShotMode(Barcode barcode)
    {
        if (_isFinishing)
            return;
        
        _isFinishing = true;
        
        var result = new BarcodeResult
        {
            Status = ScanStatus.Success,
            Symbology = barcode.Format.ToLocalFormat(),
            RawValue = barcode.RawValue,
            DisplayValue = barcode.DisplayValue,
            ScannedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        MobileBarcodeScanner.DispatchSingleResult(_instanceId, result);

        if (Looper.MainLooper != null) 
            new Handler(Looper.MainLooper).PostDelayed(Finish, _delayBeforeClose);
    }

    private void OnBarcodeVisualUpdate(Barcode? barcode)
    {
        if (barcode == null || _activeOverlay == null || string.IsNullOrWhiteSpace(barcode.RawValue)) 
             return;

        if (_isFinishing)
            return;

        var mappedPoints = GetMappedPoints(barcode);
        if (mappedPoints == null)
            return;

        var box = GetBoundingBox(mappedPoints);
        
        ApplySmoothing(barcode, box);
    }

    private void ApplySmoothing(Barcode barcode, BarcodeBox box)
    {
        const float smoothFactorCenter = 0.15f;
        const float smoothFactorSize = 0.3f;

        var key = barcode.RawValue ?? "unknown";
        lock (_lastBoxParams)
        {
            if (_lastBoxParams.TryGetValue(key, out var prev))
            {
                var dist = MathF.Sqrt((box.CenterX - prev.CenterX) * (box.CenterX - prev.CenterX) +
                                      (box.CenterY - prev.CenterY) * (box.CenterY - prev.CenterY));
                var maxDist = _cameraPreview?.Width * 0.3f ?? 0;
                var offsetWhole = System.Math.Clamp(dist / maxDist, 0, 1);
                var centerSmoothFactor = smoothFactorCenter + offsetWhole * 0.85f;

                var scx = prev.CenterX + (box.CenterX - prev.CenterX) * centerSmoothFactor;
                var scy = prev.CenterY + (box.CenterY - prev.CenterY) * centerSmoothFactor;
                var sw = prev.Width + (box.Width - prev.Width) * smoothFactorSize;
                var sh = prev.Height + (box.Height - prev.Height) * smoothFactorSize;

                var result = new BarcodeBox(scx, scy, sw, sh);
                _lastBoxParams[key] = result;

                var rectPoints = result.ToRectPoints();

                RunOnUiThread(() => { _activeOverlay?.UpdateOverlay(barcode.RawValue, rectPoints); });
            }
            else
            {
                _lastBoxParams[key] = box;
                var rectPoints = box.ToRectPoints();
                RunOnUiThread(() => { _activeOverlay?.UpdateOverlay(barcode.RawValue, rectPoints); });
            }
        }
    }

    private float[]? GetMappedPoints(Barcode barcode)
    {
        var points = barcode.GetCornerPoints();
        if (points == null || points.Length < 4) 
            return null;
         
        var srcPoints = new float[8];
        for (var i = 0; i < 4; i++)
        {
            srcPoints[i * 2] = points[i].X;
            srcPoints[i * 2 + 1] = points[i].Y;
        }

        var matrix = MatrixHelper.GetCorrectionMatrix(_latestImageProxy,
                                                      _cameraPreview,
                                                      targetMatrix: _tempMatrix);
        if (matrix == null)
            return null;

        var mappedPoints = new float[8];
        matrix.MapPoints(mappedPoints, srcPoints);
        return mappedPoints;
    }

    private BarcodeBox GetBoundingBox(float[] mappedPoints)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var x = mappedPoints[i * 2];
            var y = mappedPoints[i * 2 + 1];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        var curCenterX = (minX + maxX) / 2f;
        var curCenterY = (minY + maxY) / 2f;
        var curWidth = maxX - minX;
        var curHeight = maxY - minY;
        
        return new BarcodeBox(curCenterX, curCenterY, curWidth, curHeight);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != CAMERA_REQUEST_CODE || grantResults.Length <= 0 || grantResults[0] != Permission.Granted)
        {
            MobileBarcodeScanner.DispatchCancel(_instanceId);
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

    public static void SetTorchState(string instanceId, bool turnTorchOn)
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