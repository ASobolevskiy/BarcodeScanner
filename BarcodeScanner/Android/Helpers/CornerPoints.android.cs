using System.Runtime.InteropServices;
using Android.Graphics;

namespace BarcodeScanner.Helpers;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CornerPoints (
    Point2D TopLeft, 
    Point2D TopRight, 
    Point2D BottomRight, 
    Point2D BottomLeft)
{
    /// <summary>
    /// Creates CornerPoints from rect.
    /// </summary>
    public static CornerPoints FromRect(RectF? rect)
    {
        ArgumentNullException.ThrowIfNull(rect);
        return new CornerPoints(
                                new Point2D(rect.Left, rect.Top),
                                new Point2D(rect.Right, rect.Top),
                                new Point2D(rect.Right, rect.Bottom),
                                new Point2D(rect.Left, rect.Bottom));
    }

    /// <summary>
    /// Creates CornerPoints from flat float array.
    /// </summary>
    public static CornerPoints FromFloatArray(float[]? values)
    {
        ArgumentNullException.ThrowIfNull(values);
        
        if (values.Length != 8)
            throw new ArgumentException($"Expected 8 elements, got {values.Length}", nameof(values));
        return new CornerPoints(
                                new Point2D(values[0], values[1]),
                                new Point2D(values[2], values[3]),
                                new Point2D(values[4], values[5]),
                                new Point2D(values[6], values[7]));
    }
    
    /// <summary>
    /// Converts CornerPoints to flat float array
    /// </summary>
    /// <returns>Float array of 8 floats</returns>
    public float[] ToFloatArray() =>
    [
        TopLeft.X, TopLeft.Y,
        TopRight.X, TopRight.Y,
        BottomRight.X, BottomRight.Y,
        BottomLeft.X, BottomLeft.Y
    ];
    
    /// <summary>
    /// Gets center coordinates of a rectangle formed by CornerPoints
    /// </summary>
    /// <returns>Point2D of rect center</returns>
    public Point2D GetCenter() => 
        new Point2D(
                    (TopLeft.X + TopRight.X + BottomRight.X + BottomLeft.X) / 4f,
                    (TopLeft.Y + TopRight.Y + BottomRight.Y + BottomLeft.Y) / 4f);

    /// <summary>
    /// Calculates linear interpolation between two sets of CornerPoints. Used in animations
    /// </summary>
    /// <param name="from">Current position in CornerPoints</param>
    /// <param name="to">Target position in CornerPoints</param>
    /// <param name="progress">Current animation progress returned by animator</param>
    /// <returns>Interpolated set of CornerPoints</returns>
    public static CornerPoints Lerp(CornerPoints from, CornerPoints to, float progress) => 
        new(
            LerpPoint(from.TopLeft, to.TopLeft, progress),
            LerpPoint(from.TopRight, to.TopRight, progress),
            LerpPoint(from.BottomRight, to.BottomRight, progress),
            LerpPoint(from.BottomLeft, to.BottomLeft, progress));

    /// <summary>
    /// Calculates linear interpolation between two points.
    /// </summary>
    /// <param name="from">Current point</param>
    /// <param name="to">Target point</param>
    /// <param name="factor">Smoothing factor</param>
    /// <returns>Interpolated point</returns>
    private static Point2D LerpPoint(Point2D from, Point2D to, float factor) => 
        new(from.X + (to.X - from.X) * factor, from.Y + (to.Y - from.Y) * factor);
    
    /// <summary>
    /// Calculates a bounding box
    /// </summary>
    /// <returns>Bounding box for specified corner points</returns>
    public RectF GetBounds()
    {
        var left = MathF.Min(MathF.Min(TopLeft.X, TopRight.X), MathF.Min(BottomRight.X, BottomLeft.X));
        var top = MathF.Min(MathF.Min(TopLeft.Y, TopRight.Y), MathF.Min(BottomRight.Y, BottomLeft.Y));
        var right = MathF.Max(MathF.Max(TopLeft.X, TopRight.X), MathF.Max(BottomRight.X, BottomLeft.X));
        var bottom = MathF.Max(MathF.Max(TopLeft.Y, TopRight.Y), MathF.Max(BottomRight.Y, BottomLeft.Y));
        return new RectF(left, top, right, bottom);
    }
}

internal readonly record struct Point2D(float X, float Y)
{
    public static Point2D Lerp(Point2D from, Point2D to, float progress) =>
        new(from.X + (to.X - from.X) * progress, from.Y + (to.Y - from.Y) * progress);
}