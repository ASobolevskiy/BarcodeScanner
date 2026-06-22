using Android.Gms.Tasks;
using Android.Runtime;
using Android.Util;
using AndroidX.Camera.Core;
using BarcodeScanner.Helpers;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.Common;

namespace BarcodeScanner.Analysers;

public class BarcodeAnalyzer(
    IBarcodeScanner scanner,
    Action<Barcode?> onBarcodeDetected,
    Action<Barcode?> onVisualUpdate,
    Func<bool> isCooldownActive,
    FrameThrottler throttler,
    Action<IImageProxy> onImageInfo) : Java.Lang.Object, ImageAnalysis.IAnalyzer
{
    public Size? DefaultTargetResolution => null;
    public void Analyze(IImageProxy? proxyImage)
    {
        if (proxyImage?.Image == null || proxyImage.ImageInfo == null) 
            return;
        
        if (isCooldownActive() || !throttler.ShouldAnalyze(Android.OS.SystemClock.ElapsedRealtime()))
        {
            proxyImage.Close();
            return;
        }

        onImageInfo.Invoke(proxyImage);
        
        var inputImage = InputImage.FromMediaImage(proxyImage.Image, proxyImage.ImageInfo.RotationDegrees);
        
        scanner.Process(inputImage)
               .AddOnSuccessListener(new BarcodeSuccessListener(onBarcodeDetected, onVisualUpdate))
               .AddOnFailureListener(new FailureListener())
               .AddOnCompleteListener(new CompleteListener(proxyImage.Close));
    }
    
    private class BarcodeSuccessListener(
        Action<Barcode?> onDetected,
        Action<Barcode?> onVisualUpdate) : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            if (result is not JavaList list || list.Size() <= 0)
            {
                return;
            }

            var firstBarcode = list.Get(0) as Barcode;
            onVisualUpdate.Invoke(firstBarcode);
            onDetected.Invoke(firstBarcode);
        }
    }

    private class FailureListener : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e) { /* игнорируем */ }
    }

    private class CompleteListener(Action action) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task result) => action.Invoke();
    }
}