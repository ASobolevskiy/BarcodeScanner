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
    WeakReference<Action<List<Barcode>>> onBarcodeDetectedRef,
    WeakReference<Func<bool>> shouldProcessFrameRef,
    WeakReference<Action<IImageProxy>> onImageInfoRef)
    : Java.Lang.Object, ImageAnalysis.IAnalyzer
{
    private readonly WeakReference<IBarcodeScanner> _scannerRef = new(scanner);

    private readonly BarcodeSuccessListener _successListener = new(onBarcodeDetectedRef);
    private readonly FailureListener _failureListener = new();

    private volatile bool _isStopping;
    private int _isDisposed;
    
    public Size? DefaultTargetResolution => null;

    public void MarkAsDisposed()
    {
        _isStopping = true;
    }
    
    public void Analyze(IImageProxy? proxyImage)
    {
        if (_isStopping || _isDisposed == 1)
        {
            proxyImage?.Close();
            return;
        }
        
        if (proxyImage?.Image == null || proxyImage.ImageInfo == null) 
            return;

        if (!shouldProcessFrameRef.TryGetTarget(out var shouldProcess) || !shouldProcess())
        {
            proxyImage.Close();
            return;
        }

        if (onImageInfoRef.TryGetTarget(out var onImageInfo))
        {
            onImageInfo.Invoke(proxyImage);
        }
        
        var inputImage = InputImage.FromMediaImage(proxyImage.Image, proxyImage.ImageInfo.RotationDegrees);

        var completeListener = new CompleteListener(proxyImage);
        
        if (_scannerRef.TryGetTarget(out var scanner))
        {
            scanner.Process(inputImage)
                   .AddOnSuccessListener(_successListener)
                   .AddOnFailureListener(_failureListener)
                   .AddOnCompleteListener(completeListener);
        }
        else
        {
            proxyImage.Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;
        _isStopping = true;
        
        if (disposing)
        {
            _successListener?.MarkAsDead();
            _failureListener?.Dispose();
        }
        
        base.Dispose(disposing);
    }

    private sealed class BarcodeSuccessListener(
        WeakReference<Action<List<Barcode>>> onBarcodeDetectedRef) : Java.Lang.Object, IOnSuccessListener
    {
        private volatile bool _isDisposed;
        
        public void OnSuccess(Java.Lang.Object? result)
        {
            if (_isDisposed)
                return;
            
            var barcodes = new List<Barcode>();
            if (result is not JavaList list || list.Size() <= 0)
            {
                return;
            }

            for (int i = 0; i < list.Size(); i++)
            {
                if (list.Get(i) is Barcode barcode) barcodes.Add(barcode);
            }

            if (onBarcodeDetectedRef.TryGetTarget(out var onBarcodeDetected))
            {
                onBarcodeDetected.Invoke(barcodes);
            }
        }

        internal void MarkAsDead()
        {
            _isDisposed = true;
        }
    }

    private sealed class FailureListener : Java.Lang.Object, IOnFailureListener
    {
        private volatile bool _isDisposed;
        
        public void OnFailure(Java.Lang.Exception e)
        {
            if (_isDisposed)
                return;
        }
        
        internal void MarkAsDead()
        {
            _isDisposed = true;
        }
    }

    private sealed class CompleteListener(IImageProxy? proxyImage) : Java.Lang.Object, IOnCompleteListener
    {
        private volatile bool _isDisposed;

        public void OnComplete(Android.Gms.Tasks.Task result)
        {
            if (_isDisposed)
                return;
            try
            {
                if (proxyImage is null) 
                    return;
                try
                {
                    proxyImage.Close();
                }
                catch (ObjectDisposedException)
                {
                    System.Diagnostics.Debug.WriteLine("Proxy image already disposed");
                }
            }
            finally
            {
                _isDisposed = true;
            }
        }
    }
}