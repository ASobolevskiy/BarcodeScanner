namespace SampleProject.iOS;

public class MainViewController : UIViewController
{
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        if (View == null)
            return;
        View.BackgroundColor = UIColor.White;
        var button = UIButton.FromType(UIButtonType.RoundedRect);
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        button.SetTitle("Start Scanning", UIControlState.Normal);
        View.AddSubview(button);
        
        button.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor).Active = true;
        button.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor).Active = true;

        button.TouchUpInside += (_, _) =>
        {
            var scannerVc = new MetadataScanningController();
            scannerVc.OnBarcodeFound = (format, value) =>
            {
                Console.WriteLine($"Считан {format}: {value}");
            };
            scannerVc.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
            PresentViewController(scannerVc, true, null);
        };
    }
}