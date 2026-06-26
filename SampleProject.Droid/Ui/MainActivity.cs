using _Microsoft.Android.Resource.Designer;
using Android.Content;
using AndroidX.AppCompat.App;
using BarcodeScanner;
using BarcodeScanner.Enums;
using BarcodeScanner.Models;

namespace SampleProject.Droid;

[Activity(Label = "@string/app_name", Theme = "@style/Theme.AppCompat.Light.NoActionBar", MainLauncher = true)]
public class MainActivity : AppCompatActivity
{
    private Button? _buttonScan;
    private Button? _buttonContinuousScan;
    private Button? _buttonScanWithAutoClose;
    private Button? _buttonScanWithCustomOverlay;
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MobileBarcodeScannerPlatform.Init(this);
        SetContentView(ResourceConstant.Layout.activity_main);
        _buttonScan = FindViewById<Button>(ResourceConstant.Id.scan_button);
        _buttonContinuousScan = FindViewById<Button>(ResourceConstant.Id.continuous_scan_button);
        _buttonScanWithAutoClose = FindViewById<Button>(ResourceConstant.Id.scan_auto_close_button);
        _buttonScanWithCustomOverlay = FindViewById<Button>(ResourceConstant.Id.custom_overlay_scan);
        
        var scanner = new MobileBarcodeScanner();

        _buttonScan?.Click += async (_, _) =>
        {
            var options = new BarcodeScanningOptions
            {
                PossibleFormats = [BarcodeSymbology.DataMatrix, BarcodeSymbology.Code128, BarcodeSymbology.QrCode],
            };
            var result = await scanner.ScanAsync(options);
            HandleBarcodeResult(result);
        };

        _buttonContinuousScan?.Click += async (_, _) =>
        {
            var options = new BarcodeScanningOptions
            {
                PossibleFormats = [BarcodeSymbology.DataMatrix, BarcodeSymbology.Code128, BarcodeSymbology.QrCode],
            };
            await scanner.ScanContinuouslyAsync(options, HandleBarcodeResult);
        };

        _buttonScanWithAutoClose.Click += async (_, _) =>
        {
            var options = new BarcodeScanningOptions
            {
                PossibleFormats = [BarcodeSymbology.DataMatrix, BarcodeSymbology.Code128, BarcodeSymbology.QrCode],
                UseAutoClose = true,
                AutoCloseDelaySeconds = 10
            };
            var result = await scanner.ScanAsync(options);
            if (result != null)
            {
                switch (result.Status)
                {
                    case ScanStatus.Success:
                        HandleBarcodeResult(result);
                        break;
                    case ScanStatus.Error:
                        System.Diagnostics.Debug.WriteLine($"Error reading code: {result.ErrorMessage}");
                        break;
                    case ScanStatus.AutoClosed:
                        System.Diagnostics.Debug.WriteLine($"Scanner is closed due to autoclose timeout");
                        break;
                    case ScanStatus.CancelledByUser:
                        System.Diagnostics.Debug.WriteLine($"User cancelled scan");
                        break;
                }
            }
        };

        _buttonScanWithCustomOverlay.Click += async (_, _) =>
        {
            var options = new BarcodeScanningOptions
            {
                PossibleFormats = [BarcodeSymbology.DataMatrix, BarcodeSymbology.Code128, BarcodeSymbology.QrCode],
                CustomOverlayFactory = libContext =>
                {
                    if (libContext is not Context context)
                        throw new InvalidOperationException("Invalid context");
                    var inflater = Android.Views.LayoutInflater.From(context);

                    var overlayView = inflater?.Inflate(ResourceConstant.Layout.custom_static_overlay, null);

                    var torch = overlayView?.FindViewById(ResourceConstant.Id.flash);
                    torch?.Click += (sender, args) => scanner.ToggleTorch();

                    var back = overlayView?.FindViewById(ResourceConstant.Id.back);
                    back?.Click += (sender, args) => scanner.CancelScan();

                    return overlayView;
                }
            };

            await scanner.ScanAsync(options);
        };
    }

    
    private void HandleBarcodeResult(BarcodeResult? result)
    {
        if(result != null)
        {
            System.Diagnostics.Debug.WriteLine($"Found code: {result}\nTime scanned: {result.ScannedTime}");
        }
    }
}