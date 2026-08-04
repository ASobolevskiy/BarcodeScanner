using CoreGraphics;

namespace BarcodeScanner;

internal static class GeometryExtensions
{
    extension(CGPoint[]? points)
    {
        public float[] ToFloatArray()
        {
            if (points == null || points.Length == 0 || points.Length < 4) 
                return [];
            
            var result = new float[points.Length * 2];
            for (var i = 0; i < points.Length; i++)
            {
                result[i * 2] = (float)points[i].X;
                result[i * 2 + 1] = (float)points[i].Y;
            }
            return result;
        }
    }

    extension(CGRect rect)
    {
        public CGPoint[] ToCgPointArray()
        {
            return
            [
                new CGPoint(rect.GetMinX(), rect.GetMinY()),
                new CGPoint(rect.GetMaxX(), rect.GetMinY()),
                new CGPoint(rect.GetMinX(), rect.GetMaxY()),
                new CGPoint(rect.GetMaxX(), rect.GetMaxY())
            ];
        }
    }
}