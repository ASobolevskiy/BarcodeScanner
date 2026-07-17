namespace BarcodeScanner.Helpers;

internal readonly record struct CornerPoints(
    Point2D TopLeft,
    Point2D TopRight,
    Point2D BottomRight,
    Point2D BottomLeft)
{
    public static CornerPoints FromFloatArray(float[]? values)
    {
        if (values is null or { Length: < 8 })
        {
            throw new ArgumentException("Expected 8 elements", nameof(values));
        }

        return new CornerPoints(
                                new Point2D(values[0], values[1]),
                                new Point2D(values[2], values[3]),
                                new Point2D(values[4], values[5]),
                                new Point2D(values[6], values[7]));
    }
    
    public static CornerPoints FromRect(float left, float top, float right, float bottom)
    {
        return new CornerPoints(
                                new Point2D(left, top),
                                new Point2D(right, top),
                                new Point2D(right, bottom),
                                new Point2D(left, bottom)
                               );
    }

    public float[] ToFloatArray() =>
    [
        TopLeft.X, TopLeft.Y,
        TopRight.X, TopRight.Y,
        BottomRight.X, BottomRight.Y,
        BottomLeft.X, BottomLeft.Y
    ];

    public (float Left, float Top, float Right, float Bottom) GetBounds()
    {
        var left = MathF.Min(MathF.Min(TopLeft.X, TopRight.X), MathF.Min(BottomRight.X, BottomLeft.X));
        var top = MathF.Min(MathF.Min(TopLeft.Y, TopRight.Y), MathF.Min(BottomRight.Y, BottomLeft.Y));
        var right = MathF.Max(MathF.Max(TopLeft.X, TopRight.X), MathF.Max(BottomRight.X, BottomLeft.X));
        var bottom = MathF.Max(MathF.Max(TopLeft.Y, TopRight.Y), MathF.Max(BottomRight.Y, BottomLeft.Y));
        return (left, top, right, bottom);
    }

    public static CornerPoints Lerp(CornerPoints from, CornerPoints to, float progress)
    {
        return new CornerPoints(
                                Point2D.Lerp(from.TopLeft, to.TopLeft, progress),
                                Point2D.Lerp(from.TopRight, to.TopRight, progress),
                                Point2D.Lerp(from.BottomRight, to.BottomRight, progress),
                                Point2D.Lerp(from.BottomLeft, to.BottomLeft, progress));
    }
}

internal readonly record struct Point2D(float X, float Y)
{
    public static Point2D Lerp(Point2D from, Point2D to, float progress) => 
        new(from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress);
}