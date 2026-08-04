using BarcodeScanner.Models;

namespace BarcodeScanner;

internal static class IosRoiRectExtensions
{
    extension(RoiRect roiRect)
    {
        /// <summary>
        /// Converts relative proportions of RoiRect into absolute iOS pixels.
        /// </summary>
        /// <param name="containerWidth">The width of container that will hold ROI</param>
        /// <param name="containerHeight">The height of container that will hold ROI</param>
        /// <remarks>If RoiRect is not valid this will fall back to the whole container</remarks>
        /// <returns>RoiRect translated to CoreGraphics.CGRect</returns>
        public CGRect ToCgRect(nfloat containerWidth, nfloat containerHeight)
        {
            if (!roiRect.IsValid)
            {
                return new CGRect(0, 0, containerWidth, containerHeight);
            }
            
            var x = roiRect.Left * containerWidth;
            var y = roiRect.Top * containerHeight;
            var width = (roiRect.Right - roiRect.Left) * containerWidth;
            var height = (roiRect.Bottom - roiRect.Top) * containerHeight;
            
            return new CGRect(x, y, width, height);
        }
    }
}