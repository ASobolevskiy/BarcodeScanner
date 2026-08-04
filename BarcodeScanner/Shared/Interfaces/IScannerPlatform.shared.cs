namespace BarcodeScanner;

internal interface IScannerPlatform
{
    void CloseScanner();
    void SetTorch(bool turnOn);
}