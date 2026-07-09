using Android.Gms.Tasks;
using Android.Runtime;
using Android.Util;
using AndroidX.Camera.Core;
using BarcodeScanner.Helpers;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.Common;

namespace BarcodeScanner.Analysers;

internal sealed class BarcodeAnalyzer(
    IBarcodeScanner scanner,
    Action<List<Barcode>> onBarcodeDetected,
    Func<long, bool> shouldProcessFrame,
    Action<IImageProxy> onImageInfo) : Java.Lang.Object, ImageAnalysis.IAnalyzer
{

    private readonly BarcodeSuccessListener _successListener = new(onBarcodeDetected);
    private readonly FailureListener _failureListener = new();
    
    public Size? DefaultTargetResolution => null;
    
    public void Analyze(IImageProxy? proxyImage)
    {
        if (proxyImage?.Image == null || proxyImage.ImageInfo == null) 
            return;

        var currentTime = Android.OS.SystemClock.ElapsedRealtime();
        if (!shouldProcessFrame(currentTime))
        {
            proxyImage.Close();
            return;
        }

        onImageInfo.Invoke(proxyImage);
        
        var inputImage = InputImage.FromMediaImage(proxyImage.Image, proxyImage.ImageInfo.RotationDegrees);

        var completeListener = new CompleteListener(proxyImage.Close);
        scanner.Process(inputImage)
               .AddOnSuccessListener(_successListener)
               .AddOnFailureListener(_failureListener)
               .AddOnCompleteListener(completeListener);
    }
    
    private sealed class BarcodeSuccessListener(
        Action<List<Barcode>> onDetected) : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            var barcodes = new List<Barcode>();
            if (result is not JavaList list || list.Size() <= 0)
            {
                return;
            }

            for (int i = 0; i < list.Size(); i++)
            {
                if (list.Get(i) is Barcode barcode) barcodes.Add(barcode);
            }
            
            onDetected.Invoke(barcodes);
        }
    }

    private sealed class FailureListener : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e) { /* игнорируем */ }
    }

    private sealed class CompleteListener(Action action) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task result)
        {
            try
            {
                action.Invoke();
            }
            finally
            {
                Dispose();
            }
        }
    }
}