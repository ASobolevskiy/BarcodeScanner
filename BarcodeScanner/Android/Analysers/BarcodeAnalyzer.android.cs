using Android.Gms.Tasks;
using Android.Runtime;
using Android.Util;
using AndroidX.Camera.Core;
using BarcodeScanner.Helpers;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.Common;

namespace BarcodeScanner.Analysers;

internal sealed record FrameGeometry(int Width, int Height, int RotationDegrees);

internal sealed class BarcodeAnalyzer(
    IBarcodeScanner scanner,
    WeakReference<Action<List<Barcode>>> onBarcodeDetectedRef,
    WeakReference<Func<bool>> shouldProcessFrameRef,
    WeakReference<Action<FrameGeometry>> onImageInfoRef)
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
        // CameraX requires Analyze() to always close the ImageProxy it's given, on every exit
        // path — otherwise, with ImageAnalysis.StrategyKeepOnlyLatest, no further frames are
        // ever delivered (a silent, permanent scanning freeze) and the underlying surface/buffer
        // may not release either. handedOff is the only source of truth for whether ownership
        // was actually transferred to CompleteListener; every early return leaves it false, so
        // there is exactly one place — the finally block below — that ever closes the frame.
        var handedOff = false;
        try
        {
            if (_isStopping || _isDisposed == 1)
                return;

            if (proxyImage?.Image == null || proxyImage.ImageInfo == null)
                return;

            if (!shouldProcessFrameRef.TryGetTarget(out var shouldProcess) || !shouldProcess())
                return;

            if (onImageInfoRef.TryGetTarget(out var onImageInfo))
            {
                onImageInfo.Invoke(new FrameGeometry(proxyImage.Width, proxyImage.Height, proxyImage.ImageInfo.RotationDegrees));
            }

            var inputImage = InputImage.FromMediaImage(proxyImage.Image, proxyImage.ImageInfo.RotationDegrees);

            if (!_scannerRef.TryGetTarget(out var scanner))
                return;

            var completeListener = new CompleteListener(proxyImage);
            scanner.Process(inputImage)
                   .AddOnSuccessListener(_successListener)
                   .AddOnFailureListener(_failureListener)
                   .AddOnCompleteListener(completeListener);
            handedOff = true;
        }
        catch (Exception ex)
        {
            // Reachable if the native Image/session is torn down concurrently with an in-flight
            // Analyze() call (teardown races with the background analysis thread). Must not
            // rethrow: an unhandled exception crossing back into the CameraX/JNI caller here
            // terminates the process, not just this frame.
            Log.Warn("BarcodeAnalyzer", $"Analyze failed, dropping frame: {ex.Message}");
        }
        finally
        {
            if (!handedOff)
                SafeClose(proxyImage);
        }
    }

    private static void SafeClose(IImageProxy? proxyImage)
    {
        try
        {
            proxyImage?.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarcodeScanner] Failed to close ImageProxy: {ex.Message}");
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
            _failureListener?.MarkAsDead();
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

            Log.Warn("BarcodeAnalyzer", $"Barcode detection failed: {e.Message}");
        }
        
        internal void MarkAsDead()
        {
            _isDisposed = true;
        }
    }

    private sealed class CompleteListener(IImageProxy? proxyImage) : Java.Lang.Object, IOnCompleteListener
    {
        private int _closed;

        public void OnComplete(Android.Gms.Tasks.Task result)
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
                return;

            SafeClose(proxyImage);
        }
    }
}