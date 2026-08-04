using Android.Graphics;
using BarcodeScanner.Helpers;

namespace BarcodeScanner;

internal static class RectFExtensions
{
    extension(RectF rect)
    {
        internal CornerPoints ToCornerPoints()
        {
            return CornerPoints.FromRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        public float[] ToFloatArray()
        {
            return
            [
                rect.Left, rect.Top,
                rect.Right, rect.Top,
                rect.Right, rect.Bottom,
                rect.Left, rect.Bottom
            ];
        }
    }
}