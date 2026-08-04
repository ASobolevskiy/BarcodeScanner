using Android.Graphics;
using BarcodeScanner.Models;

namespace BarcodeScanner;

internal static class AndroidRoiRectExtensions
{
    extension(RoiRect roiRect)
    {
        /// <summary>
        /// Converts relative proportions of RoiRect into absolute Android pixels.
        /// </summary>
        /// <param name="containerWidth">The width of container that will hold ROI</param>
        /// <param name="containerHeight">The height of container that will hold ROI</param>
        /// <remarks>If RoiRect is not valid this will fall back to the whole container</remarks>
        /// <returns>RoiRect translated to Android.Graphics.RectF</returns>
        public RectF ToRectF(int containerWidth, int containerHeight)
        {
            if (!roiRect.IsValid)
            {
                return new RectF(0, 0, containerWidth, containerHeight);
            }

            return new RectF(
                             roiRect.Left * containerWidth,
                             roiRect.Top * containerHeight,
                             roiRect.Right * containerWidth,
                             roiRect.Bottom * containerHeight);
        }
    }
}