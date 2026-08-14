using BarcodeScanner;
using BarcodeScanner.Models;

namespace SampleProject.iOS;

public class MainViewController : UIViewController
{
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        if (View == null)
            return;
        View.BackgroundColor = UIColor.White;
        
        var scanOneShotButton = UIButton.FromType(UIButtonType.RoundedRect);
        scanOneShotButton.TranslatesAutoresizingMaskIntoConstraints = false;
        scanOneShotButton.SetTitle("Start Scanning", UIControlState.Normal);
        View.AddSubview(scanOneShotButton);
        
        scanOneShotButton.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor).Active = true;
        scanOneShotButton.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor).Active = true;
        
        var scanContinuousButton = UIButton.FromType(UIButtonType.RoundedRect);
        scanContinuousButton.TranslatesAutoresizingMaskIntoConstraints = false;
        scanContinuousButton.SetTitle("Continuous Scanning", UIControlState.Normal);
        View.AddSubview(scanContinuousButton);
        
        scanContinuousButton.TopAnchor.ConstraintEqualTo(scanOneShotButton.BottomAnchor, 10).Active = true;
        scanContinuousButton.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor).Active = true;
        
        var scanner = new MobileBarcodeScanner();
        
        scanOneShotButton.TouchUpInside += async (_, _) =>
        {
            var options = new BarcodeScanningOptions
            {
                PossibleFormats = [BarcodeSymbology.DataMatrix, BarcodeSymbology.Code128, BarcodeSymbology.QrCode]
            };
            var result = await scanner.ScanAsync(options);
            HandleBarcodeResult(result);
        };

        scanContinuousButton.TouchUpInside += async (_, _) =>
        {
            var options = new BarcodeScanningOptions
            {
                PossibleFormats = [BarcodeSymbology.DataMatrix, BarcodeSymbology.Code128, BarcodeSymbology.QrCode],
            };
            await scanner.ScanContinuouslyAsync(HandleBarcodeResult, options);
        };
    }

    private void HandleBarcodeResult(BarcodeResult result)
    {
        System.Diagnostics.Debug.WriteLine($"Found code: {result}\nTime scanned: {result.ScannedTime}");
    }
}