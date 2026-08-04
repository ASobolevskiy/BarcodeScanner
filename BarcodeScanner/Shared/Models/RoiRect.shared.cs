namespace BarcodeScanner.Models;

/// <summary>
/// A region of interest within the scanner screen, in coordinates relative to the screen
/// size (0.0 to 1.0 on each axis) rather than absolute pixels — (0, 0) is the top-left
/// corner, (1, 1) is the bottom-right corner. Set via
/// <see cref="BarcodeScanningOptions.RegionOfInterest"/> to restrict detection to part
/// of the frame. If <see cref="IsValid"/> is false, the value is ignored and the default
/// detection area is used instead — see <see cref="IsValid"/> for the validity rules.
/// </summary>
/// <param name="Left">Left edge, relative to screen width (0.0 to 1.0).</param>
/// <param name="Top">Top edge, relative to screen height (0.0 to 1.0).</param>
/// <param name="Right">Right edge, relative to screen width (0.0 to 1.0).</param>
/// <param name="Bottom">Bottom edge, relative to screen height (0.0 to 1.0).</param>
public readonly record struct RoiRect(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    /// <summary>
    /// Creates ROI (Region of interest) rect centered in screen
    /// </summary>
    /// <param name="sizePercent">Size percentage in float (i.e. 0.6f = 60% of screen size)</param>
    public static RoiRect CreateCentered(float sizePercent = 0.6f)
    {
        var half = sizePercent / 2f;
        return new RoiRect(0.5f - half, 0.5f - half, 0.5f + half, 0.5f + half);
    }

    /// <summary>
    /// Validates that all coordinates in [0, 1], Right > Left and Bottom > Top
    /// </summary>
    public bool IsValid =>
        Left is >= 0f and <= 1f &&
        Top is >= 0f and <= 1f &&
        Right is >= 0f and <= 1f &&
        Bottom is >= 0f and <= 1f &&
        Right > Left && Bottom > Top;
}

internal readonly struct RoiBounds(
    float left,
    float top,
    float right,
    float bottom)
{
    private float Left { get; } = left;
    private float Top { get; } = top;
    private float Right { get; } = right;
    private float Bottom { get; } = bottom;
    
    internal float CenterX => (Left + Right) * 0.5f;
    internal float CenterY => (Top + Bottom) * 0.5f;
    
    internal float Width => Right - Left;
    internal float Height => Bottom - Top;
    
    internal bool Contains(float x, float y) => 
        x >= Left && x <= Right && y >= Top && y <= Bottom;
}