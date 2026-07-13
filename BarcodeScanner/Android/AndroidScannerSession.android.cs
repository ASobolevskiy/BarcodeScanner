namespace BarcodeScanner;

internal sealed class AndroidScannerSession(
    WeakReference<IScannerPlatform> platformRef) : IPlatformScannerSession
{
    public void RequestCancel()
    {
        if (platformRef.TryGetTarget(out var platform))
        {
            platform.CloseScanner();
        }
    }

    public void SetTorch(bool turnOn)
    {
        if (platformRef.TryGetTarget(out var platform))
        {
            platform.SetTorch(turnOn);
        }
    }
}