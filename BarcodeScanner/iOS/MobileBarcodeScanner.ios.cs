using System;
using System.Threading.Tasks;
using BarcodeScanner.Models;
using UIKit;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner
{
    private partial Task<BarcodeResult?> PlatformScanSingleAsync()
    {
        var controller = new MetadataScanningController(InstanceId);
        controller.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        GetCurrentViewController().PresentViewController(controller, true, null);
        return _singleScanTcs!.Task;
    }

    private partial Task PlatformScanContinuousAsync()
    {
        var controller = new MetadataScanningController(InstanceId);
        controller.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        GetCurrentViewController().PresentViewController(controller, true, null);
        return _continuousScanTcs!.Task;
    }
    
    private UIViewController GetCurrentViewController()
    {
        UIViewController? result = null;

        result = FindTopVcFromScene(result) ?? FindTopVcFromWindow(result);

        return result ?? throw new InvalidOperationException("Не удалось получить текущий ViewController."); 
    }

    private UIViewController? FindTopVcFromScene(UIViewController? result)
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene
                {
                    ActivationState: UISceneActivationState.ForegroundActive
                    or UISceneActivationState.ForegroundInactive
                } windowScene) 
                continue;
            
            var window = windowScene.KeyWindow;
            if (window == null) 
                continue;
            result = GetTopViewController(window.RootViewController);
            if (result != null) 
                break;
        }

        return result;
    }
    
#pragma warning disable CA1422
    private UIViewController? FindTopVcFromWindow(UIViewController? result)
    {
        var window = UIApplication.SharedApplication.KeyWindow;
        if (window != null)
        {
            result = GetTopViewController(window.RootViewController);
        }

        return result;
    }
#pragma warning restore CA1422
    
    private UIViewController? GetTopViewController(UIViewController? viewController)
    {
        return viewController switch
        {
            null => null,
            UINavigationController nav => GetTopViewController(nav.TopViewController),
            UITabBarController tab => GetTopViewController(tab.SelectedViewController),
            _ => viewController.PresentedViewController != null
                ? GetTopViewController(viewController.PresentedViewController)
                : viewController
        };
    }
}