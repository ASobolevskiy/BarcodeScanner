using System;
using System.Threading.Tasks;
using BarcodeScanner.Models;
using CoreFoundation;
using Foundation;
using UIKit;

namespace BarcodeScanner;

public partial class MobileBarcodeScanner
{
    private partial void PlatformPostToMain(Action action)
    {
        if (NSThread.IsMain)
        {
            action();
            return;
        }
        DispatchQueue.MainQueue.DispatchAsync(action);
    }

    private partial void PlatformStartScanner(string sessionId)
    {
        // UIKit (GetCurrentViewController/PresentViewController) may only be touched on the
        // main thread - unlike Android's StartActivity, which is safe from any thread. Nothing
        // in the public ScanAsync/ScanContinuouslyAsync contract forbids calling from a
        // background thread, so this hop is required for parity with Android rather than
        // pushing that requirement onto every consumer (CONC-05 in CONTEXT.md). Dispatched
        // synchronously (not via PlatformPostToMain's fire-and-forget DispatchAsync) so any
        // exception thrown while presenting still propagates back to the caller's try/catch in
        // ScanAsync/ScanContinuouslyAsync instead of being lost on the main queue.
        if (NSThread.IsMain)
        {
            PresentScanner(sessionId);
            return;
        }
        DispatchQueue.MainQueue.DispatchSync(() => PresentScanner(sessionId));
    }

    private void PresentScanner(string sessionId)
    {
        var controller = new MetadataScanningController(sessionId)
        {
            ModalPresentationStyle = UIModalPresentationStyle.FullScreen
        };
        GetCurrentViewController().PresentViewController(controller, true, null);
    }
    
    private UIViewController GetCurrentViewController()
    {
        UIViewController? result = null;

        result = FindTopVcFromScene(result) ?? FindTopVcFromWindow(result);

        return result ?? throw new InvalidOperationException("Failed to obtain the current ViewController.");
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