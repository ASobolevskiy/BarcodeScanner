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

    public Point2D GetCenter() => new Point2D(
                                              (TopLeft.X + TopRight.X + BottomRight.X + BottomLeft.X) / 4f,
                                              (TopLeft.Y + TopRight.Y + BottomRight.Y + BottomLeft.Y) / 4f);
    
    public (float Left, float Top, float Right, float Bottom) GetBounds()
    {
        var left = MathF.Min(MathF.Min(TopLeft.X, TopRight.X), MathF.Min(BottomRight.X, BottomLeft.X));
        var top = MathF.Min(MathF.Min(TopLeft.Y, TopRight.Y), MathF.Min(BottomRight.Y, BottomLeft.Y));
        var right = MathF.Max(MathF.Max(TopLeft.X, TopRight.X), MathF.Max(BottomRight.X, BottomLeft.X));
        var bottom = MathF.Max(MathF.Max(TopLeft.Y, TopRight.Y), MathF.Max(BottomRight.Y, BottomLeft.Y));
        return (left, top, right, bottom);
    }

    public static CornerPoints Smooth(CornerPoints from, CornerPoints to, float centerFactor, float sizeFactor)
    {
        // var fromCenter = from.GetCenter();
        // var toCenter = to.GetCenter();
        // var smoothedCenter = Point2D.Smooth(fromCenter, toCenter, centerFactor);
        // return new CornerPoints(
        //                         InterpolateCorner(from.TopLeft, fromCenter, to.TopLeft, toCenter, smoothedCenter, sizeFactor),
        //                         InterpolateCorner(from.TopRight, fromCenter, to.TopRight, toCenter, smoothedCenter, sizeFactor),
        //                         InterpolateCorner(from.BottomRight, fromCenter, to.BottomRight, toCenter, smoothedCenter, sizeFactor),
        //                         InterpolateCorner(from.BottomLeft, fromCenter, to.BottomLeft, toCenter, smoothedCenter, sizeFactor));
        var fromBounds = from.GetBounds();
        var toBounds = to.GetBounds();
        
        var fromCenterX = (fromBounds.Left + fromBounds.Right) * 0.5f;
        var fromCenterY = (fromBounds.Top + fromBounds.Bottom) * 0.5f;
        var fromWidth = fromBounds.Right - fromBounds.Left;
        var fromHeight = fromBounds.Bottom - fromBounds.Top;

        var toCenterX = (toBounds.Left + toBounds.Right) * 0.5f;
        var toCenterY = (toBounds.Top + toBounds.Bottom) * 0.5f;
        var toWidth = toBounds.Right - toBounds.Left;
        var toHeight = toBounds.Bottom - toBounds.Top;
        
        var smoothedCenterX = fromCenterX + (toCenterX - fromCenterX) * centerFactor;
        var smoothedCenterY = fromCenterY + (toCenterY - fromCenterY) * centerFactor;
        var smoothedWidth = fromWidth + (toWidth - fromWidth) * sizeFactor;
        var smoothedHeight = fromHeight + (toHeight - fromHeight) * sizeFactor;
        
        var halfW = smoothedWidth * 0.5f;
        var halfH = smoothedHeight * 0.5f;
        
        return new CornerPoints(
                                new Point2D(smoothedCenterX - halfW, smoothedCenterY - halfH), // TopLeft
                                new Point2D(smoothedCenterX + halfW, smoothedCenterY - halfH), // TopRight
                                new Point2D(smoothedCenterX + halfW, smoothedCenterY + halfH), // BottomRight
                                new Point2D(smoothedCenterX - halfW, smoothedCenterY + halfH)  // BottomLeft
                               );
    }

    public static CornerPoints Lerp(CornerPoints from, CornerPoints to, float progress)
    {
        return new CornerPoints(
                                Point2D.Lerp(from.TopLeft, to.TopLeft, progress),
                                Point2D.Lerp(from.TopRight, to.TopRight, progress),
                                Point2D.Lerp(from.BottomRight, to.BottomRight, progress),
                                Point2D.Lerp(from.BottomLeft, to.BottomLeft, progress));
    }

    private static Point2D InterpolateCorner(Point2D fromCorner, Point2D fromCenter, Point2D toCorner, Point2D toCenter,
        Point2D smoothedCenter, float sizeFactor)
    {
        var fromVector = new Point2D(fromCorner.X - fromCenter.X, fromCorner.Y - fromCenter.Y);
        var toVector = new Point2D(toCorner.X - toCenter.X, toCorner.Y - toCenter.Y);
        var smoothedVector = Point2D.Lerp(fromVector, toVector, sizeFactor);
        return new Point2D(smoothedCenter.X + smoothedVector.X, smoothedCenter.Y + smoothedVector.Y);
    }
}

internal readonly record struct Point2D(float X, float Y)
{
    public static Point2D Lerp(Point2D from, Point2D to, float progress) => 
        new(from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress);
}