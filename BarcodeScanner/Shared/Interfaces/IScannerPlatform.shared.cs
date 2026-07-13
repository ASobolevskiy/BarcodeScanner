namespace BarcodeScanner;

public interface IScannerPlatform
{
    void CloseScanner();
    void SetTorch(bool turnOn);
}