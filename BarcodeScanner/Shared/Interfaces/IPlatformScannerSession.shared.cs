namespace BarcodeScanner;

internal interface IPlatformScannerSession
{
    void RequestCancel();
    void SetTorch(bool turnOn);
}