namespace BarcodeScanner.Models;

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