using Android.Graphics;
using BarcodeScanner.Models;

namespace BarcodeScanner;

public static class AndroidViewFinderRectExtensions
{
    extension(RectF rect)
    {
        public ViewFinderRect ToViewFinderRect() => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    extension(ViewFinderRect rect)
    {
        public RectF ToRectF() => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
}