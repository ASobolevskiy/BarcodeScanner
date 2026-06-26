using System;
using System.Collections.Generic;

namespace BarcodeScanner.Models;

public sealed class BarcodeScanningOptions
{
    /// <summary>
    /// List of symbologies you want to detect. By default, all possible symbologies included.
    /// You can narrow down the list by setting this to your own list of symbologies.
    /// </summary>
    public IEnumerable<BarcodeSymbology> PossibleFormats { get; set; } = [BarcodeSymbology.AllSymbologies];

    /// <summary>
    /// Delay in milliseconds. 1 second by default.
    /// If you are in continuous scanning mode use this to set delay between consecutive
    /// scans. 
    /// </summary>
    public int DelayBetweenContinuousScans { get; set; } = 1000;

    /// <summary>
    /// Delay in milliseconds. 150ms by default.
    /// Use this to tune delay between frame analysis
    /// </summary>
    public int DelayBetweenAnalyzingFrames { get; set; } = 150;

    /// <summary>
    /// Delay in milliseconds. 300ms by default.
    /// Use this to tune the delay before first frame will be analyzed.
    /// </summary>
    public int DelayBeforeAnalyzingFrames { get; set; } = 300;
    
    /// <summary>
    /// Factory for creating custom scanner overlay.
    /// <remarks>The library will call it for view creation passing its context (Activity or ViewController) as argument
    /// for memory leaks prevention
    /// The returning object must be an Android.Views.View or UIKit.UIView
    /// In addition if you want to create custom overlay with animations your overlay class must implement
    /// IActiveScannerOverlay interface provided by this library otherwise it will not receive updates from scan engine
    /// </remarks>
    /// </summary>
    public Func<object, object>? CustomOverlayFactory { get; set; }

    /// <summary>
    /// Delay before scanner closure after successful scan (in milliseconds)
    /// </summary>
    /// <remarks>
    /// ⚠️ WARNING: Minimal acceptable value is <b>300 ms</b>.
    /// If you try to set value below 300ms it will be automatically set
    /// to 300ms to ensure there will be no problems with animations.
    /// </remarks>
    public int DelayBeforeScannerClose
    {
        get;
        set => field = Math.Max(300, value);
    } = 500;
    
    /// <summary>
    /// Set if you want to enable automatic scanner closure.
    /// <remarks>
    /// This works only in single scan mode. Continuous scan mode does not use this feature.
    /// </remarks>
    /// </summary>
    public bool UseAutoClose { get; set; } = false;
    
    /// <summary>
    /// Delay (in seconds) before scanner will auto close. 15 seconds by default.
    /// <remarks>
    /// If you set <b>UseAutoClose = true</b> and <b>AutoCloseDelaySeconds = 0</b> automatic closure will not work.
    /// </remarks>
    /// </summary>
    public int AutoCloseDelaySeconds { get; set; } = 15;
    
    /// <summary>
    /// Region of Interest in relative coordinates (from 0.0 to 1.0).
    /// If null, the viewfinder rectangle from the overlay is used (or the entire screen if the overlay is custom).
    /// </summary>
    public RoiRect? RegionOfInterest { get; set; }

    internal ScanType ScannerMode { get; set; }
}

public enum ScanType
{
    OneShot,
    Continuous
}

