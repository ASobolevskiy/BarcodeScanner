using _Microsoft.Android.Resource.Designer;
using Android.Content;
using AndroidX.AppCompat.App;

namespace SampleProject.Droid;

[Activity(Label = "@string/app_name", Theme = "@style/Theme.AppCompat.Light.NoActionBar", MainLauncher = true)]
public class MainActivity : AppCompatActivity
{
    private const int SCAN_REQUEST_CODE = 100;
    private Button? _buttonScan;
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(ResourceConstant.Layout.activity_main);

        _buttonScan = FindViewById<Button>(ResourceConstant.Id.scan_button);
        _buttonScan?.Click += (_, _) =>
        {
            var intent = new Intent(this, typeof(ScannerActivity));
            StartActivityForResult(intent, SCAN_REQUEST_CODE);
        };
    }
    
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SCAN_REQUEST_CODE || resultCode != Result.Ok) 
            return;
        var value = data?.GetStringExtra("barcode_value");
        var format = data?.GetStringExtra("barcode_format");
        // Покажем результат в Toast или обновим UI
        Toast.MakeText(this, $"Считан {format}: {value}", ToastLength.Long)?.Show();
    }
}