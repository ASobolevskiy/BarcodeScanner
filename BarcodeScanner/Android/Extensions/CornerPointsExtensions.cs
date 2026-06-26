using Android.Graphics;
using BarcodeScanner.Helpers;

namespace BarcodeScanner;

internal static class CornerPointsExtensions
{
    extension(CornerPoints cornerPoints)
    {
        public RectF ToRectF()
        {
            var (left, top, right, bottom) = cornerPoints.GetBounds();
            return new RectF(left, top, right, bottom);
        }
    }
}