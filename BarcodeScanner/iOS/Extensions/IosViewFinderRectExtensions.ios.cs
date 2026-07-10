using BarcodeScanner.Models;

namespace BarcodeScanner;

public static class IosViewFinderRectExtensions
{
    extension(CGRect rect)
    {
        public ViewFinderRect ToViewFinderRect() => new((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
    }

    extension(ViewFinderRect rect)
    {
        public CGRect ToCgRect() => new(rect.Left, 
                                        rect.Top, 
                                        rect.Right - rect.Left, 
                                        rect.Bottom - rect.Top);
    }
}