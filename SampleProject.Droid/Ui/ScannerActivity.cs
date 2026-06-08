// using _Microsoft.Android.Resource.Designer;
// using Android.Content;
// using Android.Content.PM;
// using Android.Gms.Tasks;
// using Android.Graphics;
// using Android.OS;
// using Android.Runtime;
// using Android.Util;
// using AndroidX.AppCompat.App;
// using AndroidX.Camera.Core;
// using AndroidX.Camera.Core.ResolutionSelector;
// using AndroidX.Camera.Lifecycle;
// using AndroidX.Camera.View;
// using AndroidX.Core.App;
// using AndroidX.Core.Content;
// using BarcodeScanner.Helpers;
// using BarcodeScanner.Ui.Views;
// using Google.Common.Util.Concurrent;
// using Java.Lang;
// using Xamarin.Google.MLKit.Vision.BarCode;
// using Xamarin.Google.MLKit.Vision.Barcode.Common;
// using Xamarin.Google.MLKit.Vision.Common;
// using Exception = System.Exception;
// using Task = Android.Gms.Tasks.Task;
//
// namespace SampleProject.Droid;
//
// [Activity(Label = "ScannerActivity", Theme = "@style/Theme.AppCompat.Light.NoActionBar")]
// public class ScannerActivity : AppCompatActivity
// {
//     private PreviewView? _cameraPreview;
//     private IListenableFuture? _cameraProviderFuture;
//     private ProcessCameraProvider? _cameraProvider;
//     private IBarcodeScanner? _barcodeScanner;
//     private BarcodeScannerOverlayView? _barcodeOverlay;
//
//     private IImageProxy? _latestImageProxy;
//     private readonly Dictionary<string, (float cx, float cy, float w, float h)> _lastBoxParams = new();
//     private bool _isFinishing;
//     
//     protected override void OnCreate(Bundle? savedInstanceState)
//     {
//         base.OnCreate(savedInstanceState);
//         SetContentView(ResourceConstant.Layout.activity_scan);
//
//         _cameraPreview = FindViewById<PreviewView>(ResourceConstant.Id.camera_preview);
//         _barcodeOverlay = FindViewById<BarcodeScannerOverlayView>(ResourceConstant.Id.barcodeOverlay);
//         
//         if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.Camera) == Permission.Granted)
//         {
//             InitScanner();
//             StartCamera();
//         }
//         else
//         {
//             ActivityCompat.RequestPermissions(this, [Android.Manifest.Permission.Camera], 1001);
//         }
//     }
//
//     protected override void OnDestroy()
//     {
//         base.OnDestroy();
//         _cameraProvider?.UnbindAll();
//         _barcodeScanner?.Dispose();
//     }
//
//     public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
//     {
//         base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
//         if (requestCode != 1001 || grantResults.Length <= 0 || grantResults[0] != Permission.Granted)
//             return;
//         InitScanner();
//         StartCamera();
//     }
//
//     private void InitScanner()
//     {
//         var options = new BarcodeScannerOptions.Builder()
//             .SetBarcodeFormats(Barcode.FormatQrCode)
//             .Build();
//         _barcodeScanner = BarcodeScanning.GetClient(options);
//     }
//     
//     private void StartCamera()
//     {
//         if (_barcodeScanner == null)
//             return;
//         
//         _cameraProviderFuture = ProcessCameraProvider.GetInstance(this);
//         _cameraProviderFuture?.AddListener(new Runnable(() =>
//         {
//             try
//             {
//                 _cameraProvider = _cameraProviderFuture.Get() as ProcessCameraProvider;
//
//                 var preview = new Preview.Builder().Build() ?? throw new NullReferenceException();
//                 preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(this), _cameraPreview?.SurfaceProvider);
//
//                 var cameraSelector = CameraSelector.DefaultBackCamera ?? throw new ArgumentNullException(nameof(CameraSelector));
//
//                 var resolutionSelector = new ResolutionSelector.Builder()
//                                          .SetResolutionStrategy(new ResolutionStrategy(
//                                                                                        new Size(1280, 720),
//                                                                                        ResolutionStrategy.FallbackRuleClosestHigher))?
//                                          .Build();
//                     
//                 var imageAnalysis = new ImageAnalysis.Builder()
//                                     .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)?
//                                     .SetResolutionSelector(resolutionSelector)?
//                                     .Build() ?? throw new Exception("Image analyzer is null!");
//                 
//                 imageAnalysis.SetAnalyzer(
//                                           ContextCompat.GetMainExecutor(this),
//                                           new BarcodeAnalyzer(
//                                                               _barcodeScanner,
//                                                               OnBarcodeFound,
//                                                               OnBarcodeVisualUpdate,
//                                                               (proxy) =>
//                                                               {
//                                                                   _latestImageProxy = proxy;
//                                                               }));
//
//                 _cameraProvider?.UnbindAll();
//                 _cameraProvider?.BindToLifecycle(this, cameraSelector, preview, imageAnalysis);
//             }
//             catch (Java.Lang.Exception ex)
//             {
//                 System.Diagnostics.Debug.WriteLine($"CameraX error: {ex.Message}");
//             }
//         }), ContextCompat.GetMainExecutor(this));
//     }
//     
//     private void OnBarcodeFound(Barcode? barcode)
//     {
//         if (barcode == null || _isFinishing) return;
//
//         _isFinishing = true;
//
//         var resultIntent = new Intent();
//         resultIntent.PutExtra("barcode_value", barcode.RawValue);
//         resultIntent.PutExtra("barcode_format", barcode.Format.ToString());
//         SetResult(Result.Ok, resultIntent);
//         // Отложенное закрытие, чтобы рамка успела отрисоваться
//         new Handler(Looper.MainLooper).PostDelayed(Finish, 500); // 500 мс – комфортная пауза
//     }
//
//     private readonly Matrix _tempMatrix = new();
//
//     private void OnBarcodeVisualUpdate(Barcode? barcode)
//     {
//         if (barcode == null || _barcodeOverlay == null || string.IsNullOrEmpty(barcode.RawValue)) 
//             return;
//         
//         var points = barcode.GetCornerPoints();
//         if (points == null || points.Length < 4) 
//             return;
//         
//         var srcPoints = new float[8];
//         for (var i = 0; i < 4; i++)
//         {
//             srcPoints[i * 2] = points[i].X;
//             srcPoints[i * 2 + 1] = points[i].Y;
//         }
//
//         var matrix = MatrixHelper.GetCorrectionMatrix(_latestImageProxy,
//                                                       _cameraPreview,
//                                                       targetMatrix: _tempMatrix);
//         if (matrix == null)
//             return;
//
//         var mappedPoints = new float[8];
//         matrix.MapPoints(mappedPoints, srcPoints);
//
//         float minX = float.MaxValue, minY = float.MaxValue;
//         float maxX = float.MinValue, maxY = float.MinValue;
//         for (var i = 0; i < 4; i++)
//         {
//             var x = mappedPoints[i * 2];
//             var y = mappedPoints[i * 2 + 1];
//             if (x < minX) minX = x;
//             if (x > maxX) maxX = x;
//             if (y < minY) minY = y;
//             if (y > maxY) maxY = y;
//         }
//
//         var curCenterX = (minX + maxX) / 2f;
//         var curCenterY = (minY + maxY) / 2f;
//         var curWidth = maxX - minX;
//         var curHeight = maxY - minY;
//
//         
//         const float smoothFactorCenter = 0.15f;
//         const float smoothFactorSize = 0.3f;
//
//         var key = barcode.RawValue ?? "unknown";
//         lock (_lastBoxParams)
//         {
//             if (!_lastBoxParams.TryGetValue(key, out var prev))
//             {
//                 var dist = MathF.Sqrt((curCenterX - prev.cx) * (curCenterX - prev.cx) +
//                                         (curCenterY - prev.cy) * (curCenterY - prev.cy));
//                 var maxDist = _cameraPreview?.Width * 0.3f ?? 0;
//                 var offsetWhole = System.Math.Clamp(dist / maxDist, 0, 1);
//                 var centerSmoothFactor = smoothFactorCenter + offsetWhole * 0.85f;
//
//                 var scx = prev.cx + (curCenterX - prev.cx) * centerSmoothFactor;
//                 var scy = prev.cy + (curCenterY - prev.cy) * centerSmoothFactor;
//                 var sw = prev.w + (curWidth - prev.w) * smoothFactorSize;
//                 var sh = prev.h + (curHeight - prev.h) * smoothFactorSize;
//
//                 var result = (scx, scy, sw, sh);
//                 _lastBoxParams[key] = result;
//                 
//                 var halfW = result.sw / 2f;
//                 var halfH = result.sh / 2f;
//                 float[] rectPoints =
//                 [
//                     result.scx - halfW, result.scy - halfH, // левый верхний
//                     result.scx + halfW, result.scy - halfH, // правый верхний
//                     result.scx + halfW, result.scy + halfH, // правый нижний
//                     result.scx - halfW, result.scy + halfH // левый нижний
//                 ];
//
//                 RunOnUiThread(() => { _barcodeOverlay.UpdateOverlay(barcode.RawValue, rectPoints); });
//             }
//             else
//             {
//                 _lastBoxParams[key] = (curCenterX, curCenterY, curWidth, curHeight);
//                 var halfCurW = curWidth / 2f;
//                 var halfCurH = curHeight / 2f;
//                 float[] rectPoints =
//                 [
//                     curCenterX - halfCurW, curCenterY - halfCurH, // левый верхний
//                     curCenterX + halfCurW, curCenterY - halfCurH, // правый верхний
//                     curCenterX + halfCurW, curCenterY + halfCurH, // правый нижний
//                     curCenterX - halfCurW, curCenterY + halfCurH // левый нижний
//                 ];
//                 RunOnUiThread(() => { _barcodeOverlay.UpdateOverlay(barcode.RawValue, rectPoints); });
//             }
//         }
//     }
//
//     private class BarcodeAnalyzer(
//         IBarcodeScanner scanner,
//         Action<Barcode?> onBarcodeDetected,
//         Action<Barcode?> onBarcodeVisualUpdate,
//         Action<IImageProxy> onImageInfo) : Java.Lang.Object, ImageAnalysis.IAnalyzer
//     {
//         public void Analyze(IImageProxy? proxyImage)
//         {
//             if (proxyImage?.Image == null || proxyImage?.ImageInfo == null) 
//                 return;
//             
//             onImageInfo?.Invoke(proxyImage);
//             
//             var inputImage = InputImage.FromMediaImage(proxyImage.Image, proxyImage.ImageInfo.RotationDegrees);
//             
//             scanner.Process(inputImage)
//                    .AddOnSuccessListener(new BarcodeSuccessListener(onBarcodeDetected, onBarcodeVisualUpdate))
//                    .AddOnFailureListener(new FailureListener())
//                    .AddOnCompleteListener(new CompleteListener(proxyImage.Close));
//         }
//         
//         public Size? DefaultTargetResolution => null;
//     }
//     
//     private class BarcodeSuccessListener(
//         Action<Barcode?> onDetected,
//         Action<Barcode?> onVisualUpdate) : Java.Lang.Object, IOnSuccessListener
//     {
//         public void OnSuccess(Java.Lang.Object? result)
//         {
//             if (result is JavaList list && list.Size() > 0)
//             {
//                 var firstBarcode = list.Get(0) as Barcode;
//                 onVisualUpdate.Invoke(firstBarcode);
//                 onDetected.Invoke(firstBarcode);
//             }
//         }
//     }
//
//     private class FailureListener : Java.Lang.Object, IOnFailureListener
//     {
//         public void OnFailure(Java.Lang.Exception e) { /* игнорируем */ }
//     }
//
//     private class CompleteListener(Action action) : Java.Lang.Object, IOnCompleteListener
//     {
//         public void OnComplete(Task result) => action.Invoke();
//     }
// }